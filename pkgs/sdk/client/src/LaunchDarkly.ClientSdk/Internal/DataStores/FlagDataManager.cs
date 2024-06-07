using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Client.Subsystems;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    /// <summary>
    /// The component that maintains the state of last known flag values, and manages
    /// persistent storage if enabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The state of the <see cref="FlagDataManager"/> consists of all the
    /// <see cref="DataModel.FeatureFlag"/>s for a specific user, plus optionally persistent
    /// storage for some number of other users. 
    /// </para>
    /// <para>
    /// This is not a pluggable component - there can only be one implementation. The only
    /// piece of behavior that is platform-dependent and customizable is the implementation
    /// of persistent storage, which is represented by the <see cref="IPersistentDataStore"/>
    /// interface.
    /// </para>
    /// </remarks>
    internal sealed class FlagDataManager : IDisposable
    {
        private readonly int _maxCachedUsers;
        private readonly PersistentDataStoreWrapper _persistentStore;
        private readonly object _writerLock = new object();
        private readonly Logger _log;

        private volatile ImmutableDictionary<string, ItemDescriptor> _flags =
            ImmutableDictionary<string, ItemDescriptor>.Empty;
        private volatile ContextIndex _storeIndex = null;
        private string _currentContextId = null;

        public PersistentDataStoreWrapper PersistentStore => _persistentStore;

        public FlagDataManager(
            string mobileKey,
            PersistenceConfiguration persistenceConfiguration,
            Logger log
            )
        {
            _log = log;

            if (persistenceConfiguration is null || persistenceConfiguration.MaxCachedUsers == 0
                || persistenceConfiguration.PersistentDataStore is NullPersistentDataStore)
            {
                _persistentStore = null;
                _maxCachedUsers = 0;
            }
            else
            {
                _persistentStore = new PersistentDataStoreWrapper(
                    persistenceConfiguration.PersistentDataStore,
                    mobileKey,
                    log
                    );
                _maxCachedUsers = persistenceConfiguration.MaxCachedUsers;
                _storeIndex = _persistentStore.GetIndex();
            }
        }

        /// <summary>
        /// Attempts to retrieve cached data for the specified context, if any. This does not
        /// affect the current context/flags state.
        /// </summary>
        /// <param name="context">an evaluation context</param>
        /// <returns>that context's data from the persistent store, or null if none</returns>
        public FullDataSet? GetCachedData(Context context) =>
            _persistentStore is null ? null : _persistentStore.GetContextData(ContextIdFor(context));

        /// <summary>
        /// Replaces the current flag data and updates the current-context state, optionally
        /// updating persistent storage as well.
        /// </summary>
        /// <param name="context">the context that should become the current context</param>
        /// <param name="data">the full flag data</param>
        /// <param name="updatePersistentStorage">true to also update the flag data in
        /// persistent storage (if persistent storage is enabled)</param>
        public void Init(Context context, FullDataSet data, bool updatePersistentStorage)
        {
            var newFlags = data.Items.ToImmutableDictionary();
            IEnumerable<string> removedUserIds = null;
            var contextId = ContextIdFor(context);
            var updatedIndex = _storeIndex;

            lock (_writerLock)
            {
                _flags = newFlags;

                if (_storeIndex != null)
                {
                    updatedIndex = _storeIndex.UpdateTimestamp(contextId, UnixMillisecondTime.Now)
                        .Prune(_maxCachedUsers, out removedUserIds);
                    _storeIndex = updatedIndex;
                }

                _currentContextId = contextId;
            }

            if (_persistentStore != null)
            {
                try
                {
                    if (removedUserIds != null)
                    {
                        foreach (var oldId in removedUserIds)
                        {
                            _persistentStore.RemoveContextData(oldId);
                        }
                    }
                    if (updatePersistentStorage)
                    {
                        _persistentStore.SetContextData(contextId, data);
                    }
                    _persistentStore.SetIndex(updatedIndex);
                }
                catch (Exception e)
                {
                    LogHelpers.LogException(_log, "Failed to write to persistent store", e);
                }
            }
        }

        /// <summary>
        /// Attempts to get a flag by key from the current flags. This always uses the
        /// in-memory cache, not persistent storage.
        /// </summary>
        /// <param name="key">the flag key</param>
        /// <returns>the flag descriptor, or null if not found</returns>
        public ItemDescriptor? Get(string key) =>
            _flags.TryGetValue(key, out var item) ? item : (ItemDescriptor?)null;

        /// <summary>
        /// Returns all current flags. This always uses the in-memory cache, not
        /// persistent storage.
        /// </summary>
        /// <returns>the data set</returns>
        public FullDataSet? GetAll() =>
            new FullDataSet(_flags);

        /// <summary>
        /// Attempts to update or insert a flag.
        /// </summary>
        /// <remarks>
        /// This implements the usual versioning logic for updates: the update only succeeds if
        /// <c>data.Version</c> is greater than the version of any current data for the same key.
        /// If successful, and if persistent storage is enabled, it also updates persistent storage.
        /// Therefore <see cref="IPersistentDataStore"/> implementations do not need to implement
        /// their own version checking.
        /// </remarks>
        /// <param name="key">the flag key</param>
        /// <param name="data">the updated flag data, or a tombstone for a deleted flag</param>
        /// <returns>true if the update was done; false if it was not done due to a too-low
        /// version number</returns>
        public bool Upsert(string key, ItemDescriptor data)
        {
            var updatedFlags = _flags;
            string contextId = null;

            lock (_writerLock)
            {
                if (_flags.TryGetValue(key, out var oldItem) && oldItem.Version >= data.Version)
                {
                    return false;
                }
                updatedFlags = _flags.SetItem(key, data);
                _flags = updatedFlags;
                contextId = _currentContextId;
            }

            _persistentStore?.SetContextData(contextId, new FullDataSet(updatedFlags));
            return true;
        }

        public void Dispose() => _persistentStore?.Dispose();

        internal static string ContextIdFor(Context context) => Base64.UrlSafeSha256Hash(context.FullyQualifiedKey);
    }
}
