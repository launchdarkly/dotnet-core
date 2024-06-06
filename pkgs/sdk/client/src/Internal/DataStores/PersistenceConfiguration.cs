using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    internal sealed class PersistenceConfiguration
    {
        public IPersistentDataStore PersistentDataStore { get; }
        public int MaxCachedUsers { get; }

        internal PersistenceConfiguration(
            IPersistentDataStore persistentDataStore,
            int maxCachedUsers
            )
        {
            PersistentDataStore = persistentDataStore;
            MaxCachedUsers = maxCachedUsers;
        }
    }
}
