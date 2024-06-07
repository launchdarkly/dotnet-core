using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Client.Internal.DataStores;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client
{
    /// <summary>
    /// Provides configurable factories for the standard implementations of LaunchDarkly component interfaces.
    /// </summary>
    /// <remarks>
    /// Some of the configuration options in <see cref="ConfigurationBuilder"/> affect the entire SDK,
    /// but others are specific to one area of functionality, such as how the SDK receives feature flag
    /// updates or processes analytics events. For the latter, the standard way to specify a configuration
    /// is to call one of the static methods in <see cref="Components"/> (such as <see cref="StreamingDataSource()"/>),
    /// apply any desired configuration change to the object that that method returns (such as
    /// <see cref="StreamingDataSourceBuilder.InitialReconnectDelay(System.TimeSpan)"/>), and then use the
    /// corresponding method in <see cref="ConfigurationBuilder"/> (such as
    /// <see cref="ConfigurationBuilder.DataSource(IComponentConfigurer{IDataSource})"/>) to use that
    /// configured component in the SDK.
    /// </remarks>
    public static class Components
    {
        /// <summary>
        /// Returns a configuration builder for the SDK's networking configuration.
        /// </summary>
        /// <remarks>
        /// Passing this to <see cref="ConfigurationBuilder.Http(HttpConfigurationBuilder)"/> applies this
        /// configuration to all HTTP/HTTPS requests made by the SDK.
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(sdkKey)
        ///         .Http(
        ///             Components.HttpConfiguration()
        ///                 .ConnectTimeout(TimeSpan.FromMilliseconds(3000))
        ///         )
        ///         .Build();
        /// </code>
        /// </example>
        /// <returns>a builder</returns>
        public static HttpConfigurationBuilder HttpConfiguration() => new HttpConfigurationBuilder();

        /// <summary>
        /// Returns a configuration builder for the SDK's logging configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Passing this to <see cref="ConfigurationBuilder.Logging(LoggingConfigurationBuilder)" />,
        /// after setting any desired properties on the builder, applies this configuration to the SDK.
        /// </para>
        /// <para>
        /// For a description of the default behavior, see <see cref="LoggingConfigurationBuilder"/>.
        /// </para>
        /// <para>
        /// For more about how logging works in the SDK, see the LaunchDarkly
        /// <a href="https://docs.launchdarkly.com/sdk/features/logging#net-client-side">feature guide</a>.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .Logging(Components.Logging().Level(LogLevel.Warn)))
        ///         .Build();
        /// </code>
        /// </example>
        /// <returns>a configurable factory object</returns>
        /// <seealso cref="ConfigurationBuilder.Logging(LoggingConfigurationBuilder)" />
        /// <seealso cref="Components.Logging(ILogAdapter) "/>
        /// <seealso cref="Components.NoLogging" />
        public static LoggingConfigurationBuilder Logging() =>
            new LoggingConfigurationBuilder();

        /// <summary>
        /// Returns a configuration builder for the SDK's logging configuration, specifying the logging implementation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is a shortcut for calling <see cref="Logging()"/> and then
        /// <see cref="LoggingConfigurationBuilder.Adapter(ILogAdapter)"/>, to specify a logging implementation
        /// other than the default one. For details about the default implementation, see
        /// <see cref="Logging()"/>.
        /// </para>
        /// <para>
        /// By default, the minimum log level is <c>Info</c> (that is, <c>Debug</c> logging is
        /// disabled). This can be overridden with <see cref="LoggingConfigurationBuilder.Level(LogLevel)"/>.
        /// </para>
        /// <para>
        /// For more about log adapters, see <see cref="LoggingConfigurationBuilder.Adapter(ILogAdapter)"/>.
        /// </para>
        /// <para>
        /// For more about how logging works in the SDK, see the LaunchDarkly
        /// <a href="https://docs.launchdarkly.com/sdk/features/logging#net-client-side">feature guide</a>.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .Logging(Components.Logging(Logs.ToConsole))
        ///         .Build();
        /// </code>
        /// </example>
        /// <param name="adapter">an <c>ILogAdapter</c> for the desired logging implementation</param>
        /// <returns>a configurable factory object</returns>
        /// <seealso cref="ConfigurationBuilder.Logging(LoggingConfigurationBuilder)" />
        /// <seealso cref="LoggingConfigurationBuilder.Adapter(ILogAdapter)" />
        /// <seealso cref="Logging() "/>
        /// <seealso cref="NoLogging" />
        public static LoggingConfigurationBuilder Logging(ILogAdapter adapter) =>
            new LoggingConfigurationBuilder().Adapter(adapter);

        /// <summary>
        /// Returns a configuration object that disables analytics events.
        /// </summary>
        /// <remarks>
        /// Passing this to <see cref="ConfigurationBuilder.Events(IComponentConfigurer{IEventProcessor})"/> causes
        /// the SDK to discard all analytics events and not send them to LaunchDarkly, regardless of
        /// any other configuration.
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .Events(Components.NoEvents)
        ///         .Build();
        /// </code>
        /// </example>
        /// <seealso cref="ConfigurationBuilder.Events(IComponentConfigurer{IEventProcessor})"/>
        /// <seealso cref="SendEvents"/>
        public static IComponentConfigurer<IEventProcessor> NoEvents =>
            ComponentsImpl.NullEventProcessorFactory.Instance;

        /// <summary>
        /// A configuration object that disables logging.
        /// </summary>
        /// <remarks>
        /// This is the same as <c>Logging(LaunchDarkly.Logging.Logs.None)</c>.
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .Logging(Components.NoLogging)
        ///         .Build();
        /// </code>
        /// </example>
        /// <seealso cref="ConfigurationBuilder.Logging(LoggingConfigurationBuilder)"/>
        /// <seealso cref="Logging()"/>
        /// <seealso cref="Logging(ILogAdapter)"/>
        public static LoggingConfigurationBuilder NoLogging =>
            new LoggingConfigurationBuilder().Adapter(Logs.None);

        /// <summary>
        /// A configuration object that disables persistent storage.
        /// </summary>
        /// <remarks>
        /// This is equivalent to `Persistence().MaxCachedUsers(0)`.
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .Persistence(Components.NoPersistence)
        ///         .Build();
        /// </code>
        /// </example>
        /// <seealso cref="ConfigurationBuilder.Persistence(PersistenceConfigurationBuilder)"/>
        /// <seealso cref="Persistence"/>
        public static PersistenceConfigurationBuilder NoPersistence =>
            Persistence().Storage(NullPersistentDataStoreFactory.Instance).MaxCachedContexts(0);

        /// <summary>
        /// Returns a configuration builder for the SDK's persistent storage configuration.
        /// </summary>
        /// <remarks>
        /// Passing this to <see cref="ConfigurationBuilder.Persistence(PersistenceConfigurationBuilder)"/>,
        /// after setting any desired properties on the builder, applies this configuration to the SDK.
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(sdkKey)
        ///         .Persistence(
        ///             Components.Persistence().MaxCachedUsers(10)
        ///         )
        ///         .Build();
        /// </code>
        /// </example>
        /// <returns>a builder</returns>
        /// <seealso cref="ConfigurationBuilder.Persistence(PersistenceConfigurationBuilder)"/>
        /// <seealso cref="PersistenceConfigurationBuilder"/>
        /// <seealso cref="NoPersistence"/>
        public static PersistenceConfigurationBuilder Persistence() =>
            new PersistenceConfigurationBuilder();

        /// <summary>
        /// Returns a configurable factory for using only polling mode to get feature flag data.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is not the default behavior; by default, the SDK uses a streaming connection to receive feature flag
        /// data from LaunchDarkly. In polling mode, the SDK instead makes a new HTTP request to LaunchDarkly at regular
        /// intervals. HTTP caching allows it to avoid redundantly downloading data if there have been no changes, but
        /// polling is still less efficient than streaming and should only be used on the advice of LaunchDarkly support.
        /// </para>
        /// <para>
        /// The SDK may still use polling mode sometimes even when streaming mode is enabled, such as
        /// when an application is in the background. You do not need to specifically select polling
        /// mode in order for that to happen.
        /// </para>
        /// <para>
        /// To use only polling mode, call this method to obtain a builder, change its properties with the
        /// <see cref="PollingDataSourceBuilder"/> methods, and pass it to
        /// <see cref="ConfigurationBuilder.DataSource(IComponentConfigurer{IDataSource})"/>.
        /// </para>
        /// <para>
        /// Setting <see cref="ConfigurationBuilder.Offline(bool)"/> to <see langword="true"/> will superseded this
        /// setting and completely disable network requests.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .DataSource(Components.PollingDataSource()
        ///             .PollInterval(TimeSpan.FromSeconds(45)))
        ///         .Build();
        /// </code>
        /// </example>
        /// <returns>a builder for setting polling connection properties</returns>
        /// <see cref="StreamingDataSource"/>
        /// <see cref="ConfigurationBuilder.DataSource(IComponentConfigurer{IDataSource})"/>
        public static PollingDataSourceBuilder PollingDataSource() =>
            new PollingDataSourceBuilder();

        /// <summary>
        /// Returns a configuration builder for analytics event delivery.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default configuration has events enabled with default settings. If you want to
        /// customize this behavior, call this method to obtain a builder, change its properties
        /// with the <see cref="EventProcessorBuilder"/> methods, and pass it to
        /// <see cref="ConfigurationBuilder.Events(IComponentConfigurer{IEventProcessor})"/>.
        /// </para>
        /// <para>
        /// To completely disable sending analytics events, use <see cref="NoEvents"/> instead.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .Events(Components.SendEvents()
        ///             .Capacity(5000)
        ///             .FlushInterval(TimeSpan.FromSeconds(2)))
        ///         .Build();
        /// </code>
        /// </example>
        /// <returns>a builder for setting event properties</returns>
        /// <seealso cref="ConfigurationBuilder.Events(IComponentConfigurer{IEventProcessor})"/>
        /// <seealso cref="NoEvents"/>
        public static EventProcessorBuilder SendEvents() => new EventProcessorBuilder();

        /// <summary>
        /// Returns a builder for configuring custom service URIs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Passing this to <see cref="ConfigurationBuilder.ServiceEndpoints(ServiceEndpointsBuilder)" />,
        /// after setting any desired properties on the builder, applies this configuration to the SDK.
        /// </para>
        /// <para>
        /// Most applications will never need to use this method. The main use case is when connecting
        /// to a <a href="https://docs.launchdarkly.com/home/advanced/relay-proxy">LaunchDarkly
        /// Relay Proxy</a> instance. For more information, see <see cref="ServiceEndpointsBuilder"/>.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .ServiceEndpoints(Components.ServiceEndpoints().RelayProxy("http://my-relay-hostname:80"))
        ///         .Build();
        /// </code>
        /// </example>
        /// <returns>a configuration builder</returns>
        /// <seealso cref="ConfigurationBuilder.ServiceEndpoints(ServiceEndpointsBuilder)" />
        public static ServiceEndpointsBuilder ServiceEndpoints() => new ServiceEndpointsBuilder();
        
        /// <summary>
        /// Returns a configurable builder for the SDK's application metadata.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Passing this to <see cref="ConfigurationBuilder.ApplicationInfo"/> after setting any desired properties on the builder,
        /// applies this configuration to the SDK.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .ApplicationInfo(
        ///             Components.ApplicationInfo().ApplicationID("MyApplication").ApplicationVersion("version123abc")
        ///         )
        ///         .Build();
        /// </code>
        /// </example>
        /// <returns>a configuration builder</returns>
        public static ApplicationInfoBuilder ApplicationInfo() => new ApplicationInfoBuilder();

        /// <summary>
        /// Returns a configurable factory for using streaming mode to get feature flag data.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default, the SDK uses a streaming connection to receive feature flag data from LaunchDarkly. To use
        /// the default behavior, you do not need to call this method. However, if you want to customize the behavior
        /// of the connection, call this method to obtain a builder, change its properties with the
        /// <see cref="StreamingDataSourceBuilder"/> methods, and pass it to
        /// <see cref="ConfigurationBuilder.DataSource(IComponentConfigurer{IDataSource})"/>.
        /// </para>
        /// <para>
        /// The SDK may still use polling mode sometimes even when streaming mode is enabled, such as
        /// when an application is in the background.
        /// </para>
        /// <para>
        /// Setting <see cref="ConfigurationBuilder.Offline(bool)"/> to <see langword="true"/> will superseded this
        /// setting and completely disable network requests.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder(mobileKey)
        ///         .DataSource(Components.StreamingDataSource()
        ///             .InitialReconnectDelay(TimeSpan.FromMilliseconds(500)))
        ///         .Build();
        /// </code>
        /// </example>
        /// <returns>a builder for setting streaming connection properties</returns>
        /// <see cref="PollingDataSource"/>
        /// <see cref="ConfigurationBuilder.DataSource(IComponentConfigurer{IDataSource})"/>
        public static StreamingDataSourceBuilder StreamingDataSource() =>
            new StreamingDataSourceBuilder();
    }
}
