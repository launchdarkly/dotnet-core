using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Integration between the LaunchDarkly SDK and Consul.
    /// </summary>
    public static class Consul
    {
        /// <summary>
        /// The default value for <see cref="ConsulDataStoreBuilder.Prefix"/>.
        /// </summary>
        public static readonly string DefaultPrefix = "launchdarkly";

        /// <summary>
        /// Returns a builder object for creating a Consul-backed persistent data store.
        /// </summary>
        /// <remarks>
        /// <para>
        /// You can use methods of the builder to specify any non-default Consul options
        /// you may want, before passing the builder to
        /// <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStore})"/>.
        /// In this example, the store is configured with only the host address:
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.PersistentDataStore(
        ///                 Consul.DataStore().Address("http://my-consul-host:8500")
        ///             )
        ///         )
        ///         .Build();
        /// </code>
        /// <para>
        /// Note that the SDK also has its own options related to data storage that are configured
        /// at a different level, because they are independent of what database is being used. For
        /// instance, the builder returned by <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStore})"/>
        /// has options for caching:
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.PersistentDataStore(
        ///                 Consul.DataStore().Address("http://my-consul-host:8500")
        ///             ).CacheSeconds(15)
        ///         )
        ///         .Build();
        /// </code>
        /// </remarks>
        /// <returns>a data store configuration object</returns>
        public static ConsulDataStoreBuilder DataStore() =>
            new ConsulDataStoreBuilder();
    }
}
