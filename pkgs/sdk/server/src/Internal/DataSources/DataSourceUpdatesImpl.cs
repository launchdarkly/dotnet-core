using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataStores;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// The data source will push updates into this component. We then apply any necessary
    /// transformations before putting them into the data store; currently that just means sorting
    /// the data set for Init().
    /// </summary>
    /// <remarks>
    /// This component is also responsible for receiving updates to the data source status, broadcasting
    /// them to any status listeners, and tracking the length of any period of sustained failure.
    /// </remarks>
    internal sealed class DataSourceUpdatesImpl : IDataSourceUpdates, IDataSourceUpdatesHeaders,
        ITransactionalDataSourceUpdates
    {
        #region Private fields

        private readonly IDataStore _store;
        private readonly IDataStoreStatusProvider _dataStoreStatusProvider;
        private readonly TaskExecutor _taskExecutor;
        private readonly StateMonitor<DataSourceStatus, StateAndError> _status;
        private readonly Logger _log;
        private readonly DependencyTracker _dependencyTracker;
        private readonly DataSourceOutageTracker _outageTracker;

        private volatile bool _lastStoreUpdateFailed = false;

        #endregion

        #region Public properties

        public IDataStoreStatusProvider DataStoreStatusProvider => _dataStoreStatusProvider;

        #endregion

        #region Internal properties

        internal DataSourceStatus LastStatus => _status.Current;

        #endregion

        #region Internal events

        internal event EventHandler<DataSourceStatus> StatusChanged;

        internal event EventHandler<FlagChangeEvent> FlagChanged;

        #endregion

        #region Internal constructor

        internal DataSourceUpdatesImpl(
            IDataStore store,
            IDataStoreStatusProvider dataStoreStatusProvider,
            TaskExecutor taskExecutor,
            Logger baseLogger,
            TimeSpan? outageLoggingTimeout
        )
        {
            _store = store;
            _dataStoreStatusProvider = dataStoreStatusProvider;
            _taskExecutor = taskExecutor;
            _log = baseLogger.SubLogger(LogNames.DataSourceSubLog);

            _dependencyTracker = new DependencyTracker();

            _outageTracker = outageLoggingTimeout.HasValue
                ? new DataSourceOutageTracker(_log, outageLoggingTimeout.Value)
                : null;

            var initialStatus = new DataSourceStatus
            {
                State = DataSourceState.Initializing,
                StateSince = DateTime.Now,
                LastError = null
            };
            _status = new StateMonitor<DataSourceStatus, StateAndError>(initialStatus, MaybeUpdateStatus, _log);
        }

        #endregion

        #region IDataSourceUpdatesImpl methods

        public bool Init(FullDataSet<ItemDescriptor> allData)
        {
            return InitWithHeaders(allData, null);
        }

        public bool Upsert(DataKind kind, string key, ItemDescriptor item)
        {
            bool successfullyUpdated = false;
            try
            {
                successfullyUpdated = _store.Upsert(kind, key, item);
                _lastStoreUpdateFailed = false;
            }
            catch (Exception e)
            {
                ReportStoreFailure(e);
                return false;
            }

            if (successfullyUpdated)
            {
                _dependencyTracker.UpdateDependenciesFrom(kind, key, item);
                if (HasFlagChangeListeners())
                {
                    var affectedItems = new HashSet<KindAndKey>();
                    _dependencyTracker.AddAffectedItems(affectedItems, new KindAndKey(kind, key));
                    SendChangeEvents(affectedItems);
                }
            }

            return true;
        }

        private struct StateAndError
        {
            public DataSourceState State { get; set; }
            public DataSourceStatus.ErrorInfo? Error { get; set; }
        }

        private static DataSourceStatus? MaybeUpdateStatus(
            DataSourceStatus oldStatus,
            StateAndError update
        )
        {
            var newState =
                (update.State == DataSourceState.Interrupted && oldStatus.State == DataSourceState.Initializing)
                    ? DataSourceState.Initializing // see comment on IDataSourceUpdates.UpdateStatus
                    : update.State;

            if (newState == oldStatus.State && !update.Error.HasValue)
            {
                return null;
            }

            return new DataSourceStatus
            {
                State = newState,
                StateSince = newState == oldStatus.State ? oldStatus.StateSince : DateTime.Now,
                LastError = update.Error ?? oldStatus.LastError
            };
        }

        public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
        {
            var updated = _status.Update(new StateAndError { State = newState, Error = newError },
                out var newStatus);

            if (updated)
            {
                _outageTracker?.TrackDataSourceState(newStatus.State, newError);
                _taskExecutor.ScheduleEvent(newStatus, StatusChanged);
            }
        }

        #endregion

        #region IDisposable method

        public void Dispose()
        {
            _status.Dispose();
        }

        #endregion

        #region Internal methods

        internal async Task<bool> WaitForAsync(DataSourceState desiredState, TimeSpan timeout)
        {
            var newStatus = await _status.WaitForAsync(
                status => status.State == desiredState || status.State == DataSourceState.Off,
                timeout
            );
            return newStatus.HasValue && newStatus.Value.State == desiredState;
        }

        #endregion

        #region Private methods

        private bool HasFlagChangeListeners() => FlagChanged != null;

        private void SendChangeEvents(IEnumerable<KindAndKey> affectedItems)
        {
            var copyOfHandlers = FlagChanged;
            if (copyOfHandlers == null)
            {
                return;
            }

            var sender = this;
            foreach (var item in affectedItems)
            {
                if (item.Kind == DataModel.Features)
                {
                    var eventArgs = new FlagChangeEvent(item.Key);
                    _taskExecutor.ScheduleEvent(eventArgs, copyOfHandlers);
                }
            }
        }

        private void UpdateDependencyTrackerFromFullDataSet(FullDataSet<ItemDescriptor> allData)
        {
            _dependencyTracker.Clear();
            foreach (var e0 in allData.Data)
            {
                var kind = e0.Key;
                foreach (var e1 in e0.Value.Items)
                {
                    var key = e1.Key;
                    _dependencyTracker.UpdateDependenciesFrom(kind, key, e1.Value);
                }
            }
        }

        private ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> FullDataSetToMap(
            FullDataSet<ItemDescriptor> allData)
        {
            var builder = ImmutableDictionary.CreateBuilder<DataKind, ImmutableDictionary<string, ItemDescriptor>>();
            foreach (var e in allData.Data)
            {
                builder.Add(e.Key, e.Value.Items.ToImmutableDictionary());
            }

            return builder.ToImmutable();
        }

        private ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> ChangeSetToMap(
            ChangeSet<ItemDescriptor> changeSet)
        {
            var builder = ImmutableDictionary.CreateBuilder<DataKind, ImmutableDictionary<string, ItemDescriptor>>();
            foreach (var kindEntry in changeSet.Data)
            {
                builder.Add(kindEntry.Key, kindEntry.Value.Items.ToImmutableDictionary());
            }

            return builder.ToImmutable();
        }

        private ISet<KindAndKey> UpdateDependencyTrackerForChangesetAndDetermineChanges(
            ImmutableDictionary<DataKind, ImmutableDictionary<String, ItemDescriptor>> oldDataMap,
            ChangeSet<ItemDescriptor> changeSet)
        {
            switch (changeSet.Type)
            {
                case ChangeSetType.Full:
                    return HandleFullChangeset(oldDataMap, changeSet);
                case ChangeSetType.Partial:
                    return HandlePartialChangeset(oldDataMap, changeSet);
                case ChangeSetType.None:
                    return null;
                default:
                    return null;
            }
        }

        private ISet<KindAndKey> HandleFullChangeset(
            ImmutableDictionary<DataKind, ImmutableDictionary<String, ItemDescriptor>> oldDataMap,
            ChangeSet<ItemDescriptor> changeSet)
        {
            _dependencyTracker.Clear();
            foreach (var kindEntry in changeSet.Data)
            {
                var kind = kindEntry.Key;
                foreach (var itemEntry in kindEntry.Value.Items)
                {
                    var key = itemEntry.Key;
                    _dependencyTracker.UpdateDependenciesFrom(kind, key, itemEntry.Value);
                }
            }

            if (oldDataMap == null) return null;

            var newDataMap = ChangeSetToMap(changeSet);
            return ComputeChangedItemsForFullDataSet(oldDataMap, newDataMap);
        }

        private ISet<KindAndKey> HandlePartialChangeset(
            ImmutableDictionary<DataKind, ImmutableDictionary<String, ItemDescriptor>> oldDataMap,
            ChangeSet<ItemDescriptor> changeSet)
        {
            if (oldDataMap == null)
            {
                // Update dependencies but don't track changes when no listeners
                foreach (var kindEntry in changeSet.Data)
                {
                    var kind = kindEntry.Key;
                    foreach (var itemEntry in kindEntry.Value.Items)
                    {
                        _dependencyTracker.UpdateDependenciesFrom(kind, itemEntry.Key, itemEntry.Value);
                    }
                }

                return null;
            }

            var affectedItems = new HashSet<KindAndKey>();
            foreach (var kindEntry in changeSet.Data)
            {
                var kind = kindEntry.Key;
                foreach (var itemEntry in kindEntry.Value.Items)
                {
                    var key = itemEntry.Key;
                    _dependencyTracker.UpdateDependenciesFrom(kind, key, itemEntry.Value);
                    _dependencyTracker.AddAffectedItems(affectedItems, new KindAndKey(kind, key));
                }
            }

            return affectedItems;
        }

        private ISet<KindAndKey> ComputeChangedItemsForFullDataSet(
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> oldDataMap,
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> newDataMap
        )
        {
            ISet<KindAndKey> affectedItems = new HashSet<KindAndKey>();
            var emptyDict = ImmutableDictionary.Create<string, ItemDescriptor>();
            foreach (var kind in DataModel.AllDataKinds)
            {
                var oldItems = oldDataMap.GetValueOrDefault(kind, emptyDict);
                var newItems = newDataMap.GetValueOrDefault(kind, emptyDict);
                var allKeys = oldItems.Keys.ToImmutableHashSet().Union(newItems.Keys);
                foreach (var key in allKeys)
                {
                    var hasOldItem = oldItems.TryGetValue(key, out var oldItem);
                    var hasNewItem = newItems.TryGetValue(key, out var newItem);
                    if (!hasOldItem && !hasNewItem)
                    {
                        continue; // shouldn't be possible due to how we computed allKeys
                    }

                    if (!hasOldItem || !hasNewItem || (oldItem.Version < newItem.Version))
                    {
                        _dependencyTracker.AddAffectedItems(affectedItems, new KindAndKey(kind, key));
                    }
                    // Note that comparing the version numbers is sufficient; we don't have to compare every detail of the
                    // flag or segment configuration, because it's a basic underlying assumption of the entire LD data model
                    // that if an entity's version number hasn't changed, then the entity hasn't changed (and that if two
                    // version numbers are different, the higher one is the more recent version).
                }
            }

            return affectedItems;
        }

        private void ReportStoreFailure(Exception e)
        {
            if (!_lastStoreUpdateFailed)
            {
                _log.Warn(
                    "Unexpected data store error when trying to store an update received from the data source: {0}",
                    LogValues.ExceptionSummary(e));
                _lastStoreUpdateFailed = true;
            }

            _log.Debug(LogValues.ExceptionTrace(e));
            UpdateStatus(DataSourceState.Interrupted, new DataSourceStatus.ErrorInfo
            {
                Kind = DataSourceStatus.ErrorKind.StoreError,
                Message = e.Message,
                Time = DateTime.Now
            });
        }

        private bool ApplyToLegacyStore(ChangeSet<ItemDescriptor> sortedChangeSet)
        {
            switch (sortedChangeSet.Type)
            {
                case ChangeSetType.Full:
                    return ApplyFullChangeSetToLegacyStore(sortedChangeSet);
                case ChangeSetType.Partial:
                    return ApplyPartialChangeSetToLegacyStore(sortedChangeSet);
                case ChangeSetType.None:
                default:
                    return true;
            }
        }

        private bool ApplyFullChangeSetToLegacyStore(ChangeSet<ItemDescriptor> unsortedChangeset)
        {
            var headers = new List<KeyValuePair<string, IEnumerable<string>>>();
            if (unsortedChangeset.EnvironmentId != null)
            {
                headers.Add(new KeyValuePair<string, IEnumerable<string>>(HeaderConstants.EnvironmentId,
                    new[] { unsortedChangeset.EnvironmentId }));
            }

            return InitWithHeaders(new FullDataSet<ItemDescriptor>(unsortedChangeset.Data), headers);
        }

        private bool ApplyPartialChangeSetToLegacyStore(ChangeSet<ItemDescriptor> changeSet)
        {
            foreach (var kindItemsPair in changeSet.Data)
            {
                foreach (var item in kindItemsPair.Value.Items)
                {
                    var applySuccess = Upsert(kindItemsPair.Key, item.Key, item.Value);
                    if (!applySuccess)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void GetOldDataIfFlagChangeListeners(
            out ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> oldData)
        {
            if (HasFlagChangeListeners())
            {
                // Query the existing data if any, so that after the update we can send events for
                // whatever was changed
                var oldDataBuilder = ImmutableDictionary.CreateBuilder<DataKind,
                    ImmutableDictionary<string, ItemDescriptor>>();
                foreach (var kind in DataModel.AllDataKinds)
                {
                    var items = _store.GetAll(kind);
                    oldDataBuilder.Add(kind, items.Items.ToImmutableDictionary());
                }

                oldData = oldDataBuilder.ToImmutable();
            }
            else
            {
                oldData = null;
            }
        }

        #endregion

        #region IDataSourceUpdatesHeaders methods

        public bool InitWithHeaders(FullDataSet<ItemDescriptor> allData,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> oldData;

            try
            {
                GetOldDataIfFlagChangeListeners(out oldData);

                var sortedCollections = DataStoreSorter.SortAllCollections(allData);

                if (_store is IDataStoreMetadata storeMetadata)
                {
                    var environmentId = headers?.FirstOrDefault((item) =>
                            item.Key.ToLower() == HeaderConstants.EnvironmentId).Value
                        ?.FirstOrDefault();
                    storeMetadata.InitWithMetadata(sortedCollections, new InitMetadata(environmentId));
                }
                else
                {
                    _store.Init(sortedCollections);
                }

                _lastStoreUpdateFailed = false;
            }
            catch (Exception e)
            {
                ReportStoreFailure(e);
                return false;
            }

            // Calling Init implies that the data source is now in a valid state.
            UpdateStatus(DataSourceState.Valid, null);

            // We must always update the dependency graph even if we don't currently have any event listeners, because if
            // listeners are added later, we don't want to have to reread the whole data store to compute the graph
            UpdateDependencyTrackerFromFullDataSet(allData);

            // Now, if we previously queried the old data because someone is listening for flag change events, compare
            // the versions of all items and generate events for those (and any other items that depend on them)
            if (oldData != null)
            {
                SendChangeEvents(ComputeChangedItemsForFullDataSet(oldData, FullDataSetToMap(allData)));
            }

            return true;
        }

        #endregion

        #region ITransactionalDataSourceUpdates methods

        private bool ApplyToTransactionalStore(ITransactionalDataStore transactionalDataStore,
            ChangeSet<ItemDescriptor> changeSet)
        {
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> oldData;
            // Getting the old values requires accessing the store, which can fail.
            // If there is a failure to read the store, then we stop treating it as a failure.
            try
            {
                GetOldDataIfFlagChangeListeners(out oldData);
            }
            catch (Exception e)
            {
                ReportStoreFailure(e);
                return false;
            }

            var sortedChangeSet = DataStoreSorter.SortChangeset(changeSet);

            try
            {
                transactionalDataStore.Apply(sortedChangeSet);
                _lastStoreUpdateFailed = false;
            }
            catch (Exception e)
            {
                ReportStoreFailure(e);
                return false;
            }

            // Calling Apply implies that the data source is now in a valid state.
            UpdateStatus(DataSourceState.Valid, null);

            var changes = UpdateDependencyTrackerForChangesetAndDetermineChanges(oldData, sortedChangeSet);

            // Now, if we previously queried the old data because someone is listening for flag change events, compare
            // the versions of all items and generate events for those (and any other items that depend on them)
            if (changes != null)
            {
                SendChangeEvents(changes);
            }

            return true;
        }

        public bool Apply(ChangeSet<ItemDescriptor> changeSet)
        {
            if (_store is ITransactionalDataStore transactionalDataStore)
            {
                return ApplyToTransactionalStore(transactionalDataStore, changeSet);
            }

            // Legacy update path for non-transactional stores
            return ApplyToLegacyStore(changeSet);
        }

        #endregion
    }
}
