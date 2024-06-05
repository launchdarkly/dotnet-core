using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal.DataStores;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Client.Subsystems;

using static LaunchDarkly.Sdk.Client.DataModel;
using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    internal sealed class DataSourceUpdateSinkImpl : IDataSourceUpdateSink
    {
        private readonly FlagDataManager _dataStore;
        private readonly object _lastValuesLock = new object();
        private readonly TaskExecutor _taskExecutor;

        private volatile ImmutableDictionary<string, ImmutableDictionary<string, FeatureFlag>> _lastValues =
            ImmutableDictionary<string, ImmutableDictionary<string, FeatureFlag>>.Empty;

        private readonly StateMonitor<DataSourceStatus, StateAndError> _status;
        internal DataSourceStatus CurrentStatus => _status.Current;

        internal event EventHandler<FlagValueChangeEvent> FlagValueChanged;
        internal event EventHandler<DataSourceStatus> StatusChanged;

        internal DataSourceUpdateSinkImpl(
            FlagDataManager dataStore,
            bool isConfiguredOffline,
            TaskExecutor taskExecutor,
            Logger log
            )
        {
            _dataStore = dataStore;
            _taskExecutor = taskExecutor;
            var initialStatus = new DataSourceStatus
            {
                State = isConfiguredOffline ? DataSourceState.SetOffline : DataSourceState.Initializing,
                StateSince = DateTime.Now,
                LastError = null
            };
            _status = new StateMonitor<DataSourceStatus, StateAndError>(initialStatus, MaybeUpdateStatus, log);
        }

        public void Init(Context context, FullDataSet data)
        {
            _dataStore.Init(context, data, true);

            ImmutableDictionary<string, FeatureFlag> oldValues, newValues;
            var contextKey = context.FullyQualifiedKey;
            lock (_lastValuesLock)
            {
                _lastValues.TryGetValue(contextKey, out oldValues);
                var builder = ImmutableDictionary.CreateBuilder<string, FeatureFlag>();
                foreach (var newEntry in data.Items)
                {
                    var newFlag = newEntry.Value.Item;
                    if (newFlag != null)
                    {
                        builder.Add(newEntry.Key, newFlag);
                    }
                }
                newValues = builder.ToImmutable();
                _lastValues = _lastValues.SetItem(contextKey, newValues);
            }

            UpdateStatus(DataSourceState.Valid, null);

            if (oldValues != null)
            {
                List<FlagValueChangeEvent> events = new List<FlagValueChangeEvent>();

                foreach (var newEntry in newValues)
                {
                    var newFlag = newEntry.Value;
                    if (oldValues.TryGetValue(newEntry.Key, out var oldFlag))
                    {
                        if (newFlag.Variation != oldFlag.Variation)
                        {
                            events.Add(new FlagValueChangeEvent(newEntry.Key,
                                oldFlag.Value, newFlag.Value, false));
                        }
                    }
                    else
                    {
                        events.Add(new FlagValueChangeEvent(newEntry.Key,
                            LdValue.Null, newFlag.Value, false));
                    }
                }
                foreach (var oldEntry in oldValues)
                {
                    if (!newValues.ContainsKey(oldEntry.Key))
                    {
                        events.Add(new FlagValueChangeEvent(oldEntry.Key,
                            oldEntry.Value.Value, LdValue.Null, true));
                    }
                }
                foreach (var e in events)
                {
                    _taskExecutor.ScheduleEvent(e, FlagValueChanged);
                }
            }
        }

        public void Upsert(Context context, string flagKey, ItemDescriptor data)
        {
            var updated = _dataStore.Upsert(flagKey, data);
            if (!updated)
            {
                return;
            }

            FeatureFlag oldFlag = null;
            var contextKey = context.FullyQualifiedKey;
            lock (_lastValuesLock)
            {
                _lastValues.TryGetValue(contextKey, out var oldValues);
                if (oldValues is null)
                {
                    // didn't have any flags for this user
                    var initValues = ImmutableDictionary<string, FeatureFlag>.Empty;
                    if (data.Item != null)
                    {
                        initValues = initValues.SetItem(flagKey, data.Item);
                    }
                    _lastValues = _lastValues.SetItem(contextKey, initValues);
                    return; // don't bother with change events if we had no previous data
                }
                oldValues.TryGetValue(flagKey, out oldFlag);
                var newValues = data.Item is null ?
                    oldValues.Remove(flagKey) : oldValues.SetItem(flagKey, data.Item);
                _lastValues = _lastValues.SetItem(contextKey, newValues);
            }
            if (oldFlag?.Variation != data.Item?.Variation)
            {
                var eventArgs = new FlagValueChangeEvent(flagKey,
                    oldFlag?.Value ?? LdValue.Null,
                    data.Item?.Value ?? LdValue.Null,
                    data.Item is null
                    );
                _taskExecutor.ScheduleEvent(eventArgs, FlagValueChanged);
            }
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
                ? DataSourceState.Initializing  // see comment on IDataSourceUpdateSink.UpdateStatus
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
                _taskExecutor.ScheduleEvent(newStatus, StatusChanged);
            }
        }

        internal async Task<bool> WaitForAsync(DataSourceState desiredState, TimeSpan timeout)
        {
            var newStatus = await _status.WaitForAsync(
                status => status.State == desiredState || status.State == DataSourceState.Shutdown,
                timeout
                );
            return newStatus.HasValue && newStatus.Value.State == desiredState;
        }
    }
}
