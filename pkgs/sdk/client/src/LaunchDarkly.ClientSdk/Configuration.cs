using System;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;
using LaunchDarkly.Sdk.Client.PlatformSpecific;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client
{
    /// <summary>
    /// Configuration options for <see cref="LdClient"/>.
    /// </summary>
    /// <remarks>
    /// Instances of <see cref="Configuration"/> are immutable once created. They can be created with the factory method
    /// <see cref="Configuration.Default(string,ConfigurationBuilder.AutoEnvAttributes)"/>, or using a builder pattern
    /// with <see cref="ImmutableArray{T}.Builder"/> or <see cref="Configuration.Builder(Configuration)"/>.
    /// </remarks>
    public sealed class Configuration
    {
        /// <summary>
        /// ApplicationInfo configuration which contains info about the application the SDK is running in.
        /// </summary>
        public ApplicationInfoBuilder ApplicationInfo { get; }

        /// <summary>
        /// True if Auto Environment Attributes functionality is enabled.  When enabled, the SDK will automatically
        /// provide data about the environment where the application is running.
        /// </summary>
        public bool AutoEnvAttributes { get; }

        /// <summary>
        /// Default value for <see cref="PollingDataSourceBuilder.BackgroundPollInterval"/> and
        /// <see cref="StreamingDataSourceBuilder.BackgroundPollInterval"/>.
        /// </summary>
        public static readonly TimeSpan DefaultBackgroundPollInterval = TimeSpan.FromMinutes(60);

        /// <summary>
        /// Minimum value for <see cref="PollingDataSourceBuilder.BackgroundPollInterval"/> and
        /// <see cref="StreamingDataSourceBuilder.BackgroundPollInterval"/>.
        /// </summary>
        public static readonly TimeSpan MinimumBackgroundPollInterval = TimeSpan.FromMinutes(15);

        // Settable only for testing
        internal IBackgroundModeManager BackgroundModeManager { get; }
        internal IConnectivityStateManager ConnectivityStateManager { get; }

        /// <summary>
        /// A factory object that creates an implementation of <see cref="IDataSource"/>, which will
        /// receive feature flag data.
        /// </summary>
        /// <seealso cref="ConfigurationBuilder.DataSource(IComponentConfigurer{IDataSource})"/>
        public IComponentConfigurer<IDataSource> DataSource { get; }

        /// <summary>
        /// True if diagnostic events have been disabled.
        /// </summary>
        /// <seealso cref="ConfigurationBuilder.DiagnosticOptOut(bool)"/>
        public bool DiagnosticOptOut { get; }

        /// <summary>
        /// Whether to enable feature flag updates when the application is running in the background.
        /// </summary>
        /// <remarks>
        /// This is only relevant on mobile platforms.
        /// </remarks>
        /// <seealso cref="ConfigurationBuilder.EnableBackgroundUpdating(bool)"/>
        public bool EnableBackgroundUpdating { get; }

        /// <summary>
        /// True if LaunchDarkly should provide additional information about how flag values were
        /// calculated.
        /// </summary>
        /// <remarks>
        /// The additional information will then be available through the client's "detail"
        /// methods such as <see cref="LdClient.BoolVariationDetail(string, bool)"/>. Since this
        /// increases the size of network requests, such information is not sent unless you set this option
        /// to <see langword="true"/>.
        /// </remarks>
        /// <seealso cref="ConfigurationBuilder.EvaluationReasons(bool)"/>
        public bool EvaluationReasons { get; }

        /// <summary>
        /// A factory object that creates an implementation of <see cref="IEventProcessor"/>, responsible
        /// for sending analytics events.
        /// </summary>
        /// <seealso cref="ConfigurationBuilder.Events(IComponentConfigurer{IEventProcessor})"/>
        public IComponentConfigurer<IEventProcessor> Events { get; }

        /// <summary>
        /// True if the SDK should provide unique keys for anonymous contexts.
        /// </summary>
        /// <seealso cref="ConfigurationBuilder.GenerateAnonymousKeys(bool)"/>
        public bool GenerateAnonymousKeys { get; }

        /// <summary>
        /// HTTP configuration properties for the SDK.
        /// </summary>
        /// <seealso cref="ConfigurationBuilder.Http(Integrations.HttpConfigurationBuilder)"/>
        public HttpConfigurationBuilder HttpConfigurationBuilder { get; }

        /// <summary>
        /// Logging configuration properties for the SDK.
        /// </summary>
        /// <seealso cref="Logging"/>
        public LoggingConfigurationBuilder LoggingConfigurationBuilder { get; }

        /// <summary>
        /// The key for your LaunchDarkly environment.
        /// </summary>
        /// <remarks>
        /// This should be the "mobile key" field for the environment on your LaunchDarkly dashboard.
        /// </remarks>
        public string MobileKey { get; }

        /// <summary>
        /// Whether or not this client is offline. If <see langword="true"/>, no calls to LaunchDarkly will be made.
        /// </summary>
        /// <seealso cref="ConfigurationBuilder.Offline(bool)"/>
        public bool Offline { get; }

        /// <summary>
        /// Persistent storage configuration properties for the SDK.
        /// </summary>
        /// <seealso cref="ConfigurationBuilder.Persistence(Integrations.PersistenceConfigurationBuilder)"/>
        public PersistenceConfigurationBuilder PersistenceConfigurationBuilder { get; }

        /// <summary>
        /// Defines the base service URIs used by SDK components.
        /// </summary>
        /// <seealso cref="ConfigurationBuilder.ServiceEndpoints(ServiceEndpointsBuilder)"/>
        public ServiceEndpoints ServiceEndpoints { get; }

        /// <summary>
        /// Creates a configuration with all parameters set to the default.
        /// </summary>
        /// <param name="mobileKey">the SDK key for your LaunchDarkly environment</param>
        /// <param name="autoEnvAttributes">Enable / disable Auto Environment Attributes functionality.  When enabled,
        /// the SDK will automatically provide data about the environment where the application is running.
        /// This data makes it simpler to target your mobile customers based on application name or version, or on
        /// device characteristics including manufacturer, model, operating system, locale, and so on. We recommend
        /// enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a> for
        /// more details.</param>
        /// <returns>a <see cref="Configuration"/> instance</returns>
        public static Configuration Default(string mobileKey, ConfigurationBuilder.AutoEnvAttributes autoEnvAttributes)
        {
            return Builder(mobileKey, autoEnvAttributes).Build();
        }

        /// <summary>
        /// Creates a <see cref="ConfigurationBuilder"/> for constructing a configuration object using a fluent syntax.
        /// </summary>
        /// <remarks>
        /// This is the only method for building a <see cref="Configuration"/> if you are setting properties
        /// besides the <c>MobileKey</c>. The <see cref="ConfigurationBuilder"/> has methods for setting any number of
        /// properties, after which you call <see cref="ConfigurationBuilder.Build"/> to get the resulting
        /// <c>Configuration</c> instance.
        /// </remarks>
        /// <example>
        /// <code>
        ///     var config = Configuration.Builder("my-sdk-key")
        ///         .EventFlushInterval(TimeSpan.FromSeconds(90))
        ///         .StartWaitTime(TimeSpan.FromSeconds(5))
        ///         .Build();
        /// </code>
        /// </example>
        /// <param name="mobileKey">the mobile SDK key for your LaunchDarkly environment</param>
        /// <param name="autoEnvAttributes">Enable / disable Auto Environment Attributes functionality.  When enabled,
        /// the SDK will automatically provide data about the environment where the application is running.
        /// This data makes it simpler to target your mobile customers based on application name or version, or on
        /// device characteristics including manufacturer, model, operating system, locale, and so on. We recommend
        /// enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a> for
        /// more details.</param>
        /// <returns>a builder object</returns>
        public static ConfigurationBuilder Builder(string mobileKey,
            ConfigurationBuilder.AutoEnvAttributes autoEnvAttributes)
        {
            if (String.IsNullOrEmpty(mobileKey))
            {
                throw new ArgumentOutOfRangeException(nameof(mobileKey), "key is required");
            }

            return new ConfigurationBuilder(mobileKey, autoEnvAttributes);
        }

        /// <summary>
        /// Creates a <see cref="ConfigurationBuilder"/> starting with the properties of an existing <see cref="Configuration"/>.
        /// </summary>
        /// <param name="fromConfiguration">the configuration to copy</param>
        /// <returns>a builder object</returns>
        public static ConfigurationBuilder Builder(Configuration fromConfiguration)
        {
            return new ConfigurationBuilder(fromConfiguration);
        }

        internal Configuration(ConfigurationBuilder builder)
        {
            ApplicationInfo = builder._applicationInfo;
            AutoEnvAttributes = builder._autoEnvAttributes;
            DataSource = builder._dataSource;
            DiagnosticOptOut = builder._diagnosticOptOut;
            EnableBackgroundUpdating = builder._enableBackgroundUpdating;
            EvaluationReasons = builder._evaluationReasons;
            Events = builder._events;
            GenerateAnonymousKeys = builder._generateAnonymousKeys;
            HttpConfigurationBuilder = builder._httpConfigurationBuilder;
            LoggingConfigurationBuilder = builder._loggingConfigurationBuilder;
            MobileKey = builder._mobileKey;
            Offline = builder._offline;
            PersistenceConfigurationBuilder = builder._persistenceConfigurationBuilder;
            ServiceEndpoints = (builder._serviceEndpointsBuilder ?? Components.ServiceEndpoints()).Build();
            BackgroundModeManager = builder._backgroundModeManager;
            ConnectivityStateManager = builder._connectivityStateManager;
        }
    }
}
