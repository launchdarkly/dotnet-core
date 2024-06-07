using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    internal sealed class NullPersistentDataStoreFactory : IComponentConfigurer<IPersistentDataStore>
    {
        internal static readonly NullPersistentDataStoreFactory Instance = new NullPersistentDataStoreFactory();

        public IPersistentDataStore Build(LdClientContext context) =>
            NullPersistentDataStore.Instance;
    }

    internal sealed class NullPersistentDataStore : IPersistentDataStore
    {
        internal static readonly NullPersistentDataStore Instance = new NullPersistentDataStore();

        public string GetValue(string storageNamespace, string key) => null;

        public void SetValue(string storageNamespace, string key, string value) { }

        public void Dispose() { }
    }
}
