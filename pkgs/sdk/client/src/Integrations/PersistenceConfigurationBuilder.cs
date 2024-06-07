using LaunchDarkly.Sdk.Client.Internal.DataStores;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    /// <summary>
    /// Contains methods for configuring the SDK's persistent storage behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The persistent storage mechanism allows the SDK to immediately access the last known flag data
    /// for the user, if any, if it was started offline or has not yet received data from LaunchDarkly.
    /// </para>
    /// <para>
    /// By default, the SDK uses a persistence mechanism that is specific to each platform, as
    /// described in <see cref="Storage(IComponentConfigurer{IPersistentDataStore})"/>. To use a custom persistence
    /// implementation, or to customize related properties defined in this class, create a builder with
    /// <see cref="Components.Persistence"/>, change its properties with the methods of this class, and
    /// pass it to <see cref="ConfigurationBuilder.Persistence(PersistenceConfigurationBuilder)"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    ///     var config = Configuration.Builder(sdkKey)
    ///         .Persistence(
    ///             Components.Persistence().MaxCachedUsers(5)
    ///         )
    ///         .Build();
    /// </code>
    /// </example>
    public sealed class PersistenceConfigurationBuilder
    {
        /// <summary>
        /// Default value for <see cref="MaxCachedContexts(int)"/>: 5.
        /// </summary>
        public const int DefaultMaxCachedContexts = 5;

        /// <summary>
        /// Passing this value (or any negative number) to <see cref="MaxCachedContexts(int)"/>
        /// means there is no limit on cached user data.
        /// </summary>
        public const int UnlimitedCachedContexts = -1;

        private IComponentConfigurer<IPersistentDataStore> _storeFactory = null;
        private int _maxCachedContexts = DefaultMaxCachedContexts;

        internal PersistenceConfigurationBuilder() { }

        /// <summary>
        /// Sets the storage implementation.
        /// </summary>
        /// <remarks>
        /// By default, the SDK uses a persistence mechanism that is specific to each platform: on Android and
        /// iOS it is the native preferences store, and in the .NET Standard implementation for desktop apps
        /// it is the <c>System.IO.IsolatedStorage</c> API. You may use this method to specify a custom
        /// implementation using a factory object.
        /// </remarks>
        /// <param name="persistentDataStoreFactory">a factory for the custom storage implementation, or
        /// <see langword="null"/> to use the default implementation</param>
        /// <returns>the builder</returns>
        public PersistenceConfigurationBuilder Storage(IComponentConfigurer<IPersistentDataStore> persistentDataStoreFactory)
        {
            _storeFactory = persistentDataStoreFactory;
            return this;
        }

        /// <summary>
        /// Sets the maximum number of users to store flag data for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A value greater than zero means that the SDK will use persistent storage to remember the last
        /// known flag values for up to that number of unique user keys. If the limit is exceeded, the SDK
        /// discards the data for the least recently used user.
        /// </para>
        /// <para>
        /// A value of zero means that the SDK will not use persistent storage; it will only have whatever
        /// flag data it has received since the current <c>LdClient</c> instance was started.
        /// </para>
        /// <para>
        /// A value of <see cref="UnlimitedCachedContexts"/> or any other negative number means there is no
        /// limit. Use this mode with caution, as it could cause the size of mobile device preferences to
        /// grow indefinitely if your application uses many different user keys on the same device.
        /// </para>
        /// </remarks>
        /// <param name="maxCachedContexts"></param>
        /// <returns>the builder</returns>
        public PersistenceConfigurationBuilder MaxCachedContexts(int maxCachedContexts)
        {
            _maxCachedContexts = maxCachedContexts;
            return this;
        }

        internal PersistenceConfiguration Build(LdClientContext clientContext) =>
            new PersistenceConfiguration(
                _storeFactory is null ? PlatformSpecific.LocalStorage.Instance :
                    _storeFactory.Build(clientContext),
                _maxCachedContexts
                );
    }
}
