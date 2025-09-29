using System.Net.Http;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Sdk.Helpers;

namespace LaunchDarkly.Sdk.Client
{
    /// <summary>
    /// A mutable object that uses the Builder pattern to specify properties for a <see cref="Configuration"/> object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Obtain an instance of this class by calling <see cref="Configuration.Builder(string, AutoEnvAttributes)"/>.
    /// </para>
    /// <para>
    /// All of the builder methods for setting a configuration property return a reference to the same builder, so they can be
    /// chained together.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    ///     var config = Configuration.Builder("my-mobile-key").AllAttributesPrivate(true).EventCapacity(1000).Build();
    /// </code>
    /// </example>
    public sealed class ConfigurationBuilder
    {
        /// <summary>
        /// Enable / disable options for Auto Environment Attributes functionality.  When enabled, the SDK will automatically
        /// provide data about the environment where the application is running. This data makes it simpler to target
        /// your mobile customers based on application name or version, or on device characteristics including manufacturer,
        /// model, operating system, locale, and so on. We recommend enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a>
        /// for more details.
        /// For example, consider a “dark mode” feature being added to an app. Versions 10 through 14 contain early,
        /// incomplete versions of the feature. These versions are available to all customers, but the “dark mode” feature is only
        /// enabled for testers.  With version 15, the feature is considered complete. With Auto Environment Attributes enabled,
        /// you can use targeting rules to enable "dark mode" for all customers who are using version 15 or greater, and ensure
        /// that customers on previous versions don't use the earlier, unfinished version of the feature.
        /// </summary>
        public enum AutoEnvAttributes
        {
            /// <summary>
            /// Enables the Auto EnvironmentAttributes functionality.
            /// </summary>
            Enabled,

            /// <summary>
            /// Disables the Auto EnvironmentAttributes functionality.
            /// </summary>
            Disabled
        }

        // This exists so that we can distinguish between leaving the HttpMessageHandler property unchanged
        // and explicitly setting it to null. If the property value is the exact same instance as this, we
        // will replace it with a platform-specific implementation.
        internal static readonly HttpMessageHandler DefaultHttpMessageHandlerInstance = new HttpClientHandler();

        internal ApplicationInfoBuilder _applicationInfo;
        internal bool _autoEnvAttributes = false;
        internal IComponentConfigurer<IDataSource> _dataSource = null;
        internal bool _diagnosticOptOut = false;
        internal bool _enableBackgroundUpdating = true;
        internal bool _evaluationReasons = false;
        internal IComponentConfigurer<IEventProcessor> _events = null;
        internal bool _generateAnonymousKeys = false;
        internal HttpConfigurationBuilder _httpConfigurationBuilder = null;
        internal LoggingConfigurationBuilder _loggingConfigurationBuilder = null;
        internal string _mobileKey;
        internal bool _offline = false;
        internal PersistenceConfigurationBuilder _persistenceConfigurationBuilder = null;
        internal ServiceEndpointsBuilder _serviceEndpointsBuilder = null;

        // Internal properties only settable for testing
        internal IBackgroundModeManager _backgroundModeManager;
        internal IConnectivityStateManager _connectivityStateManager;

        /// <summary>
        /// Sets the mobile key only if it passes validation rules.
        /// </summary>
        private void SetMobileKeyIfValid(string mobileKey)
        {
            if (ValidationUtils.ValidateStringValue(mobileKey) == null)
            {
                _mobileKey = mobileKey;
            }
        }

        internal ConfigurationBuilder(string mobileKey, AutoEnvAttributes autoEnvAttributes)
        {
            SetMobileKeyIfValid(mobileKey);
            _autoEnvAttributes = autoEnvAttributes == AutoEnvAttributes.Enabled; // map enum to boolean
        }

        internal ConfigurationBuilder(Configuration copyFrom)
        {
            _applicationInfo = copyFrom.ApplicationInfo;
            _autoEnvAttributes = copyFrom.AutoEnvAttributes;
            _dataSource = copyFrom.DataSource;
            _diagnosticOptOut = copyFrom.DiagnosticOptOut;
            _enableBackgroundUpdating = copyFrom.EnableBackgroundUpdating;
            _evaluationReasons = copyFrom.EvaluationReasons;
            _events = copyFrom.Events;
            _httpConfigurationBuilder = copyFrom.HttpConfigurationBuilder;
            _loggingConfigurationBuilder = copyFrom.LoggingConfigurationBuilder;
            // The mobile key from Configuration should already be valid, but we validate just in case
            SetMobileKeyIfValid(copyFrom.MobileKey);
            _offline = copyFrom.Offline;
            _persistenceConfigurationBuilder = copyFrom.PersistenceConfigurationBuilder;
            _serviceEndpointsBuilder = new ServiceEndpointsBuilder(copyFrom.ServiceEndpoints);
        }

        /// <summary>
        /// Creates a <see cref="Configuration"/> based on the properties that have been set on the builder.
        /// Modifying the builder after this point does not affect the returned <see cref="Configuration"/>.
        /// </summary>
        /// <returns>the configured <c>Configuration</c> object</returns>
        public Configuration Build()
        {
            return new Configuration(this);
        }

        /// <summary>
        /// Sets the SDK's application metadata, which may be used in the LaunchDarkly analytics or other product
        /// features.  This object is normally a configuration builder obtained from <see cref="Components.ApplicationInfo"/>,
        /// which has methods for setting individual metadata properties.
        /// </summary>
        /// <param name="applicationInfo">builder for <see cref="ApplicationInfo"/></param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder ApplicationInfo(ApplicationInfoBuilder applicationInfo)
        {
            _applicationInfo = applicationInfo;
            return this;
        }

        /// <summary>
        /// Specifies whether the SDK will use Auto Environment Attributes functionality.  When enabled,
        /// the SDK will automatically provide data about the environment where the application is running.
        /// This data makes it simpler to target your mobile customers based on application name or version, or on
        /// device characteristics including manufacturer, model, operating system, locale, and so on. We recommend
        /// enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a> for
        /// more details.
        /// </summary>
        /// <param name="autoEnvAttributes">Enable / disable Auto Environment Attributes functionality.</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder AutoEnvironmentAttributes(AutoEnvAttributes autoEnvAttributes)
        {
            _autoEnvAttributes = autoEnvAttributes == AutoEnvAttributes.Enabled; // map enum to boolean
            return this;
        }

        /// <summary>
        /// Sets the implementation of the component that receives feature flag data from LaunchDarkly,
        /// using a factory object.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Depending on the implementation, the factory may be a builder that allows you to set other
        /// configuration options as well.
        /// </para>
        /// <para>
        /// The default is <see cref="Components.StreamingDataSource"/>. You may instead use
        /// <see cref="Components.PollingDataSource"/>. See those methods for details on how
        /// to configure them.
        /// </para>
        /// <para>
        /// This overwrites any previous options set with <see cref="DataSource(IComponentConfigurer{IDataSource})"/>.
        /// If you want to set multiple options, set them on the same <see cref="StreamingDataSourceBuilder"/>
        /// or <see cref="PollingDataSourceBuilder"/>.
        /// </para>
        /// </remarks>
        /// <param name="dataSourceConfig">the factory object</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder DataSource(IComponentConfigurer<IDataSource> dataSourceConfig)
        {
            _dataSource = dataSourceConfig;
            return this;
        }

        /// <summary>
        /// Specifies whether true to opt out of sending diagnostic events.
        /// </summary>
        /// <remarks>
        /// Unless this is set to <see langword="true"/>, the client will send some
        /// diagnostics data to the LaunchDarkly servers in order to assist in the development
        /// of future SDK improvements. These diagnostics consist of an initial payload
        /// containing some details of SDK in use, the SDK's configuration, and the platform the
        /// SDK is being run on, as well as payloads sent periodically with information on
        /// irregular occurrences such as dropped events.
        /// </remarks>
        /// <param name="diagnosticOptOut"><see langword="true"/> to disable diagnostic events</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder DiagnosticOptOut(bool diagnosticOptOut)
        {
            _diagnosticOptOut = diagnosticOptOut;
            return this;
        }

        /// <summary>
        /// Sets whether to enable feature flag polling when the application is in the background.
        /// </summary>
        /// <remarks>
        /// By default, on Android and iOS the SDK can still receive feature flag updates when an application
        /// is in the background, but it will use polling rather than maintaining a streaming connection (and
        /// will use the background polling interval rather than the regular polling interval). If you set
        /// this property to false, it will not check for feature flag updates until the application returns
        /// to the foreground.
        /// </remarks>
        /// <param name="enableBackgroundUpdating"><see langword="true"/> if background updating should be allowed</param>
        /// <returns>the same builder</returns>
        /// <seealso cref="StreamingDataSourceBuilder.BackgroundPollInterval"/>
        /// <seealso cref="PollingDataSourceBuilder.BackgroundPollInterval"/>
        public ConfigurationBuilder EnableBackgroundUpdating(bool enableBackgroundUpdating)
        {
            _enableBackgroundUpdating = enableBackgroundUpdating;
            return this;
        }

        /// <summary>
        /// Set to <see langword="true"/> if LaunchDarkly should provide additional information about how flag values were
        /// calculated.
        /// </summary>
        /// <remarks>
        /// The additional information will then be available through the client's "detail"
        /// methods such as <see cref="LdClient.BoolVariationDetail(string, bool)"/>. Since this
        /// increases the size of network requests, such information is not sent unless you set this option
        /// to <see langword="true"/>.
        /// </remarks>
        /// <param name="evaluationReasons"><see langword="true"/> if evaluation reasons are desired</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder EvaluationReasons(bool evaluationReasons)
        {
            _evaluationReasons = evaluationReasons;
            return this;
        }

        /// <summary>
        /// Sets the implementation of the component that processes analytics events.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default is <see cref="Components.SendEvents"/>, but you may choose to set it to a customized
        /// <see cref="EventProcessorBuilder"/>, a custom implementation (for instance, a test fixture), or
        /// disable events with <see cref="Components.NoEvents"/>.
        /// </para>
        /// <para>
        /// This overwrites any previous options set with <see cref="Events(IComponentConfigurer{IEventProcessor})"/>.
        /// If you want to set multiple options, set them on the same <see cref="EventProcessorBuilder"/>.
        /// </para>
        /// </remarks>
        /// <param name="eventsConfig">a builder/factory object for event configuration</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder Events(IComponentConfigurer<IEventProcessor> eventsConfig)
        {
            _events = eventsConfig;
            return this;
        }

        /// <summary>
        /// Set to <see langword="true"/> to make the SDK provide unique keys for anonymous contexts.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If enabled, this option changes the SDK's behavior whenever the <see cref="Context"/> (as given to
        /// methods like <see cref="LdClient.Init(string, AutoEnvAttributes, Context, System.TimeSpan)"/> or
        /// <see cref="LdClient.Identify(Context, System.TimeSpan)"/>) has an <see cref="Context.Anonymous"/>
        /// property of <see langword="true"/>, as follows:
        /// </para>
        /// <list type="bullet">
        /// <item><description> The first time this happens in the application, the SDK will generate a
        /// pseudo-random GUID and overwrite the context's <see cref="Context.Key"/> with this string.
        /// </description></item>
        /// <item><description> The SDK will then cache this key so that the same key will be reused next time.
        /// </description></item>
        /// <item><description>This uses the same mechanism as the caching of flag values, so if persistent storage
        /// is available (see <see cref="Components.Persistence"/>), the key will persist across restarts; otherwise,
        /// it will persist only during the lifetime of the <c>LdClient</c>. </description></item>
        /// </list>
        /// <para>
        /// If you use multiple <see cref="ContextKind"/>s, this behavior is per-kind: that is, a separate
        /// randomized key is generated and cached for each context kind.
        /// </para>
        /// <para>
        /// A <see cref="Context"/> must always have a key, even if the key will later be overwritten by the
        /// SDK, so if you use this functionality you must still provide a placeholder key. This ensures that if
        /// the SDK configuration is changed so <see cref="GenerateAnonymousKeys(bool)"/> is no longer enabled,
        /// the SDK will still be able to use the context for evaluations.
        /// </para>
        /// </remarks>
        /// <param name="generateAnonymousKeys">true to enable automatic anonymous key generation</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder GenerateAnonymousKeys(bool generateAnonymousKeys)
        {
            _generateAnonymousKeys = generateAnonymousKeys;
            return this;
        }

        /// <summary>
        /// Sets the SDK's networking configuration, using a configuration builder obtained from
        /// <see cref="Components.HttpConfiguration()"/>. The builder has methods for setting
        /// individual HTTP-related properties.
        /// </summary>
        /// <remarks>
        /// This overwrites any previous options set with <see cref="Http(HttpConfigurationBuilder)"/>.
        /// If you want to set multiple options, set them on the same <see cref="HttpConfigurationBuilder"/>.
        /// </remarks>
        /// <param name="httpConfigurationBuilder">a builder for HTTP configuration</param>
        /// <returns>the top-level builder</returns>
        public ConfigurationBuilder Http(HttpConfigurationBuilder httpConfigurationBuilder)
        {
            _httpConfigurationBuilder = httpConfigurationBuilder;
            return this;
        }

        /// <summary>
        /// Sets the SDK's logging destination.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is a shortcut for <c>Logging(Components.Logging(logAdapter))</c>. You can use it when you
        /// only want to specify the basic logging destination, and do not need to set other log properties.
        /// </para>
        /// <para>
        /// For more about how logging works in the SDK, see the LaunchDarkly
        /// <a href="https://docs.launchdarkly.com/sdk/features/logging#net-client-side">feature guide</a>.
        /// </para>
        /// </remarks>
        /// <example>
        ///     var config = Configuration.Builder("my-sdk-key")
        ///         .Logging(Logs.ToWriter(Console.Out))
        ///         .Build();
        /// </example>
        /// <param name="logAdapter">an <c>ILogAdapter</c> for the desired logging implementation</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder Logging(ILogAdapter logAdapter) =>
            Logging(Components.Logging(logAdapter));

        /// <summary>
        /// Sets the SDK's logging configuration, using a configuration builder obtained from
        /// <see cref="Components.Logging()"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// As a shortcut for disabling logging, you may use <see cref="Components.NoLogging"/> instead.
        /// If all you want to do is to set the basic logging destination, and you do not need to set other
        /// logging properties, you can use <see cref="Logging(ILogAdapter)"/> instead.
        /// </para>
        /// <para>
        /// For more about how logging works in the SDK, see the LaunchDarkly
        /// <a href="https://docs.launchdarkly.com/sdk/features/logging#net-client-side">feature guide</a>.
        /// </para>
        /// <para>
        /// This overwrites any previous options set with <see cref="Logging(LoggingConfigurationBuilder)"/>.
        /// If you want to set multiple options, set them on the same <see cref="LoggingConfigurationBuilder"/>.
        /// </para>
        /// </remarks>
        /// <example>
        ///     var config = Configuration.Builder("my-sdk-key")
        ///         .Logging(Components.Logging().Level(LogLevel.Warn)))
        ///         .Build();
        /// </example>
        /// <param name="loggingConfigurationBuilder">a builder for logging configuration</param>
        /// <returns>the top-level builder</returns>
        /// <seealso cref="Components.Logging()" />
        /// <seealso cref="Components.Logging(ILogAdapter) "/>
        /// <seealso cref="Components.NoLogging" />
        /// <seealso cref="Logging(ILogAdapter)"/>
        public ConfigurationBuilder Logging(LoggingConfigurationBuilder loggingConfigurationBuilder)
        {
            _loggingConfigurationBuilder = loggingConfigurationBuilder;
            return this;
        }

        /// <summary>
        /// Sets the key for your LaunchDarkly environment.
        /// The key will not be updated if the provided key contains invalid characters.
        /// </summary>
        /// <remarks>
        /// This should be the "mobile key" field for the environment on your LaunchDarkly dashboard.
        /// </remarks>
        /// <param name="mobileKey">the mobile key</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder MobileKey(string mobileKey)
        {
            SetMobileKeyIfValid(mobileKey);
            return this;
        }

        /// <summary>
        /// Sets whether or not this client is offline. If <see langword="true"/>, no calls to LaunchDarkly will be made.
        /// </summary>
        /// <param name="offline"><see langword="true"/> if the client should remain offline</param>
        /// <returns>the same builder</returns>
        public ConfigurationBuilder Offline(bool offline)
        {
            _offline = offline;
            return this;
        }

        /// <summary>
        /// Sets the SDK's persistent storage configuration, using a configuration builder obtained from
        /// <see cref="Components.Persistence()"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The persistent storage mechanism allows the SDK to immediately access the last known flag data
        /// for the user, if any, if it is offline or has not yet received data from LaunchDarkly.
        /// </para>
        /// <para>
        /// By default, the SDK uses a persistence mechanism that is specific to each platform: on Android and
        /// iOS it is the native preferences store, and in the .NET Standard implementation for desktop apps
        /// it is the <c>System.IO.IsolatedStorage</c> API. You may use the builder methods to substitute a
        /// custom implementation or change related parameters.
        /// </para>
        /// <para>
        /// This overwrites any previous options set with this method. If you want to set multiple options,
        /// set them on the same <see cref="PersistenceConfigurationBuilder"/>.
        /// </para>
        /// </remarks>
        /// <example>
        ///     var config = Configuration.Builder("my-sdk-key")
        ///         .Persistence(Components.Persistence().MaxCachedUsers(10))
        ///         .Build();
        /// </example>
        /// <param name="persistenceConfigurationBuilder">a builder for persistence configuration</param>
        /// <returns>the top-level builder</returns>
        /// <seealso cref="Components.Persistence()" />
        /// <seealso cref="Components.NoPersistence" />
        /// <seealso cref="PersistenceConfigurationBuilder"/>
        public ConfigurationBuilder Persistence(PersistenceConfigurationBuilder persistenceConfigurationBuilder)
        {
            _persistenceConfigurationBuilder = persistenceConfigurationBuilder;
            return this;
        }

        /// <summary>
        /// Sets the SDK's service URIs, using a configuration builder obtained from
        /// <see cref="Components.ServiceEndpoints"/>.
        /// </summary>
        /// <remarks>
        /// This overwrites any previous options set with <see cref="ServiceEndpoints(ServiceEndpointsBuilder)"/>.
        /// If you want to set multiple options, set them on the same <see cref="ServiceEndpointsBuilder"/>.
        /// </remarks>
        /// <param name="serviceEndpointsBuilder">the subconfiguration builder object</param>
        /// <returns>the main configuration builder</returns>
        /// <seealso cref="Components.ServiceEndpoints"/>
        /// <seealso cref="ServiceEndpointsBuilder"/>
        public ConfigurationBuilder ServiceEndpoints(ServiceEndpointsBuilder serviceEndpointsBuilder)
        {
            _serviceEndpointsBuilder = serviceEndpointsBuilder;
            return this;
        }

        // The following properties are internal and settable only for testing.

        internal ConfigurationBuilder BackgroundModeManager(IBackgroundModeManager backgroundModeManager)
        {
            _backgroundModeManager = backgroundModeManager;
            return this;
        }

        internal ConfigurationBuilder ConnectivityStateManager(IConnectivityStateManager connectivityStateManager)
        {
            _connectivityStateManager = connectivityStateManager;
            return this;
        }
    }
}
