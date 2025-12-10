using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal class ReadonlyStoreFacade : IReadOnlyStore
    {
        private readonly IDataStore _store;

        public bool Initialized() => _store.Initialized();

        public DataStoreTypes.InitMetadata GetMetadata()
        {
            if (_store is IDataStoreMetadata metadataStore)
            {
                return metadataStore.GetMetadata();
            }

            return null;
        }

        internal ReadonlyStoreFacade(IDataStore store)
        {
            _store = store;
        }

        public DataStoreTypes.ItemDescriptor? Get(DataStoreTypes.DataKind kind, string key)
        {
            return _store.Get(kind, key);
        }

        public DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor> GetAll(DataStoreTypes.DataKind kind)
        {
            return _store.GetAll(kind);
        }
    }
}
