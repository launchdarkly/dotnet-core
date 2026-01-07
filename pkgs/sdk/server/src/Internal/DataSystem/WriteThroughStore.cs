using System;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal class WriteThroughStore : IDataStore, ITransactionalDataStore
    {
        private readonly IDataStore _memoryStore;
        private readonly ITransactionalDataStore _txMemoryStore;
        private readonly IDataStore _persistentStore;
        private bool _disposed = false;

        private readonly bool _hasPersistence;

        private readonly object _activeStoreLock = new object();
        private volatile IDataStore _activeReadStore;

        /// <summary>
        /// Indicates that this store has received a payload which would result in an initialized store.
        /// This is an independent concept of the store being initialized, as a persistent store may be initialized
        /// before receiving such a payload.
        /// <para>
        /// If we are using a persistent store, and we have some data sources, then this is a marker to switch to
        /// the in-memory store. This transition happens once, and then subsequently we only use the memory store.
        /// </para>
        /// </summary>
        private readonly AtomicBoolean _hasReceivedAnInitializingPayload = new AtomicBoolean(false);
        
        private readonly DataSystemConfiguration.DataStoreMode _persistenceMode;

        public WriteThroughStore(IDataStore memoryStore, IDataStore persistentStore, DataSystemConfiguration.DataStoreMode persistenceMode)
        {
            _memoryStore = memoryStore;
            _txMemoryStore = (ITransactionalDataStore)_memoryStore;
            _persistentStore = persistentStore;
            _hasPersistence = persistentStore != null;
            // During initializations read will happen from the persistent store.
            _activeReadStore = _hasPersistence ? _persistentStore : _memoryStore;
            _persistenceMode = persistenceMode;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;
            if (_disposed) return;
            _memoryStore.Dispose();
            _persistentStore?.Dispose();
            _disposed = true;
        }

        public bool StatusMonitoringEnabled => _persistentStore?.StatusMonitoringEnabled ?? false;

        public void Init(DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> allData)
        {
            _memoryStore.Init(allData);
            MaybeSwitchStore();

            if (_persistenceMode == DataSystemConfiguration.DataStoreMode.ReadWrite)
            {
                _persistentStore?.Init(allData);
            }
        }

        public DataStoreTypes.ItemDescriptor? Get(DataStoreTypes.DataKind kind, string key)
        {
            return _activeReadStore.Get(kind, key);
        }

        public DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor> GetAll(DataStoreTypes.DataKind kind)
        {
            return _activeReadStore.GetAll(kind);
        }

        public bool Upsert(DataStoreTypes.DataKind kind, string key, DataStoreTypes.ItemDescriptor item)
        {
            var result = _memoryStore.Upsert(kind, key, item);
            if (_hasPersistence && _persistenceMode == DataSystemConfiguration.DataStoreMode.ReadWrite)
            {
                result &= _persistentStore.Upsert(kind, key, item);
            }
            
            // We aren't going to switch from persistence on an update.
            // Currently, an upsert should not ever be the first operation on a store.
            // If selector support for persistent stores was added, then they would use the apply path.
            return result;
        }

        public bool Initialized()
        {
            return _activeReadStore.Initialized();
        }

        public void Apply(DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> changeSet)
        {
            _txMemoryStore.Apply(changeSet);
            MaybeSwitchStore();

            if (!_hasPersistence || _persistenceMode != DataSystemConfiguration.DataStoreMode.ReadWrite) return;

            if (_persistentStore is ITransactionalDataStore txPersistentStore)
            {
                txPersistentStore.Apply(changeSet);
            }
            else
            {
                // If an apply fails at init, that will throw on its own, but if it fails via an upsert, then
                // we need to throw something to work with the current data source updates implementation.
                if (!ApplyToLegacyPersistence(changeSet))
                {
                    // The exception type doesn't matter here, as it will be converted to data store status.
                    throw new Exception("Failure to apply data set to persistent store.");
                }
            }
        }

        public Selector Selector => _txMemoryStore.Selector;

        private void MaybeSwitchStore()
        {
            if (_hasReceivedAnInitializingPayload.GetAndSet(true)) return;
            lock (_activeStoreLock)
            {
                _activeReadStore = _memoryStore;

                // Disable the persistent store's cache since reads are now going through memory store
                if (_persistentStore is IExternalDataSourceSupport externalSupport)
                {
                    externalSupport.DisableCache();
                }
            }
        }

        private bool ApplyToLegacyPersistence(DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> sortedChangeSet)
        {
            // Data will have been sorted by data source updates.
            switch (sortedChangeSet.Type)
            {
                case DataStoreTypes.ChangeSetType.Full:
                    ApplyFullChangeSetToLegacyStore(sortedChangeSet);
                    break;
                case DataStoreTypes.ChangeSetType.Partial:
                    return ApplyPartialChangeSetToLegacyStore(sortedChangeSet);
                case DataStoreTypes.ChangeSetType.None:
                default:
                    break;
            }

            return true;
        }

        private void ApplyFullChangeSetToLegacyStore(
            DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> sortedChangeSet)
        {
            _persistentStore.Init(new DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor>(sortedChangeSet.Data));
        }

        private bool ApplyPartialChangeSetToLegacyStore(
            DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> sortedChangeset)
        {
            foreach (var kindItemsPair in sortedChangeset.Data)
            {
                foreach (var item in kindItemsPair.Value.Items)
                {
                    var applySuccess = _persistentStore.Upsert(kindItemsPair.Key, item.Key, item.Value);
                    if (!applySuccess)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
