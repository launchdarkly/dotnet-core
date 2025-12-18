using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// A set of different data system modes which provided pre-configured <see cref="DataSystemBuilder"/>s.
    /// <para>
    /// This class is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is not suitable for production usage. Do not use it. You have been warned.
    /// </para>
    /// </summary>
    public sealed class DataSystemModes
    {
        // This implementation is non-static to allow for easy usage with "Components".
        // Where we can return an instance of this object, and the user can chain into their desired configuration.

        /// <summary>
        /// Configure's LaunchDarkly's recommended flag data acquisition strategy.
        ///<para>
        /// Currently, it operates a two-phase method for getting data: first, it requests data from LaunchDarkly's
        /// global CDN. Then, it initiates a streaming connection to LaunchDarkly's Flag Delivery services to receive
        /// real-time updates. If the streaming connection is interrupted for an extended period of time, the SDK will
        /// automatically fall back to polling the global CDN for updates.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <example>
        /// <code>
        /// var config = Configuration.Builder("my-sdk-key")
        ///   .DataSystem(Components.DataSystem().Default());
        /// </code>
        /// </example>
        /// </remarks>
        /// <returns>a builder containing our default configuration</returns>
        public DataSystemBuilder Default()
        {
            return Custom()
                .Initializers(DataSystemComponents.Polling())
                .Synchronizers(DataSystemComponents.Streaming(), DataSystemComponents.Polling())
                .FDv1FallbackSynchronizer(DataSystemComponents.FDv1Polling());
        }

        /// <summary>
        /// Configures the SDK to stream data without polling for the initial payload.
        /// <para>
        /// This is not our recommended strategy, which is <see cref="DataSystemModes.Default"/>, but it may be
        /// suitable for some situations.
        /// </para>
        /// </summary>
        /// <remarks>
        /// This configuration will not automatically fall back to polling, but it can be instructed by LaunchDarkly
        /// to fall back to polling in certain situations.
        /// </remarks>
        /// <remarks>
        /// <example>
        /// <code>
        /// var config = Configuration.Builder("my-sdk-key")
        ///   .DataSystem(Components.DataSystem().Streaming());
        /// </code>
        /// </example>
        /// </remarks>
        /// <returns>a builder containing a primarily streaming configuration</returns>
        public DataSystemBuilder Streaming()
        {
            return Custom()
                .Synchronizers(DataSystemComponents.Streaming())
                .FDv1FallbackSynchronizer(DataSystemComponents.FDv1Polling());
        }

        /// <summary>
        /// Configured the SDK to poll data instead of receiving real-time updates via a stream.
        /// <para>
        /// This is not our recommended strategy, which is <see cref="DataSystemModes.Default"/>, but it may be
        /// required for certain network configurations.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <example>
        /// <code>
        /// var config = Configuration.Builder("my-sdk-key")
        ///   .DataSystem(Components.DataSystem().Polling());
        /// </code>
        /// </example>
        /// </remarks>
        /// <returns>a builder containing a polling-only configuration</returns>
        public DataSystemBuilder Polling()
        {
            return Custom()
                .Synchronizers(DataSystemComponents.Polling())
                .FDv1FallbackSynchronizer(DataSystemComponents.FDv1Polling());
        }

        /// <summary>
        /// Configures the SDK to read from a persistent store integration that is populated by Relay Proxy
        /// or other SDKs. The SDK will not connect to LaunchDarkly. In this mode, the SDK never writes to the data
        /// store.
        /// </summary>
        /// <remarks>
        /// <example>
        /// <code>
        /// var config = Configuration.Builder("my-sdk-key")
        ///   .DataSystem(Components.DataSystem().Daemon());
        /// </code>
        /// </example>
        /// </remarks>
        /// <returns>a builder which is configured for daemon mode</returns>
        public DataSystemBuilder Daemon(IComponentConfigurer<IDataStore> persistentStore)
        {
            return Custom()
                .PersistentStore(persistentStore,
                    DataSystemConfiguration.DataStoreMode.ReadOnly);
        }

        /// <summary>
        /// PersistentStore is similar to Default, with the addition of a persistent store integration. Before data has
        /// arrived from LaunchDarkly, the SDK is able to evaluate flags using data from the persistent store.
        /// Once fresh data is available, the SDK will no longer read from the persistent store, although it will keep
        /// it up to date.
        /// </summary>
        /// <remarks>
        /// <example>
        /// <code>
        /// var config = Configuration.Builder("my-sdk-key")
        ///   .DataSystem(Components.DataSystem().
        ///     PersistentStore(Components.PersistentDataStore(SomeDatabaseName.DataStore()))););
        /// </code>
        /// </example>
        /// </remarks>
        /// <returns>a builder which is configured for persistent store mode</returns>
        public DataSystemBuilder PersistentStore(IComponentConfigurer<IDataStore> persistentStore)
        {
            return Default()
                .PersistentStore(persistentStore, DataSystemConfiguration.DataStoreMode.ReadWrite);
        }

        /// <summary>
        /// Custom returns a builder suitable for creating a custom data acquisition strategy. You may configure
        /// how the SDK uses a Persistent Store, how the SDK obtains an initial set of data, and how the SDK keeps data
        /// up to date.
        /// </summary>
        /// <remarks>
        /// <example>
        /// <code>
        /// var config = Configuration.Builder("my-sdk-key")
        ///   .DataSystem(Components.DataSystem().Custom()
        ///     .Initializers(DataSystemComponents.Polling())
        ///     .Synchronizers(DataSystemComponents.Streaming(), DataSystemComponents.Polling())
        ///     .FDv1FallbackSynchronizer(DataSystemComponents.FDv1Polling());
        /// </code>
        /// </example>
        /// </remarks>
        /// <returns>a builder without any base configuration</returns>
        public DataSystemBuilder Custom()
        {
            return new DataSystemBuilder();
        }
    }
}
