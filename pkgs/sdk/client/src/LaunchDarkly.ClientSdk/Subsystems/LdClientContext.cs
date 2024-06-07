using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Client.PlatformSpecific;
using LaunchDarkly.Sdk.EnvReporting;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Events;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    /// <summary>
    /// Encapsulates SDK client context when creating components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The component factory interface <see cref="IComponentConfigurer{T}"/> receives this class as a parameter.
    /// Its public properties provide information about the SDK configuration and environment. The SDK
    /// may also include non-public properties that are relevant only when creating one of the built-in
    /// component types and are not accessible to custom components.
    /// </para>
    /// </remarks>
    public sealed class LdClientContext
    {
        /// <summary>
        /// The configured mobile key.
        /// </summary>
        public string MobileKey { get; private set; }

        /// <summary>
        /// The configured logger for the SDK.
        /// </summary>
        public Logger BaseLogger { get; private set; }

        /// <summary>
        /// The current evaluation context.
        /// </summary>
        public Context CurrentContext { get; private set; }

        /// <summary>
        /// A component that <see cref="IDataSource"/> implementations use to deliver status updates to
        /// the SDK.
        /// </summary>
        /// <remarks>
        /// This property is only set when the SDK is calling an <see cref="IDataSource"/> factory.
        /// Otherwise it is null.
        /// </remarks>
        public IDataSourceUpdateSink DataSourceUpdateSink { get; private set; }

        /// <summary>
        /// Whether to enable feature flag updates when the application is running in the background.
        /// </summary>
        public bool EnableBackgroundUpdating { get; private set; }

        /// <summary>
        /// True if evaluation reasons are enabled.
        /// </summary>
        public bool EvaluationReasons { get; private set; }

        /// <summary>
        /// The HTTP configuration properties.
        /// </summary>
        public HttpConfiguration Http { get; private set; }

        /// <summary>
        /// True if the application is currently in a background state.
        /// </summary>
        public bool InBackground { get; private set; }

        /// <summary>
        /// The configured service base URIs.
        /// </summary>
        public ServiceEndpoints ServiceEndpoints { get; private set; }

        /// <summary>
        /// The environment reporter.
        /// </summary>
        internal IEnvironmentReporter EnvironmentReporter { get; private set; }

        internal IDiagnosticDisabler DiagnosticDisabler { get; private set; }

        internal IDiagnosticStore DiagnosticStore { get; private set; }

        internal TaskExecutor TaskExecutor { get; private set; }

        /// <summary>
        /// Creates an instance.
        /// </summary>
        /// <param name="configuration">the SDK configuration</param>
        /// <param name="currentContext">the current evaluation context</param>
        /// <param name="eventSender"></param>
        public LdClientContext(
            Configuration configuration,
            Context currentContext,
            object eventSender = null
        )
        {
            var logger = MakeLogger(configuration);
            var environmentReporter = MakeEnvironmentReporter(configuration);

            MobileKey = configuration.MobileKey;
            BaseLogger = logger;
            CurrentContext = currentContext;
            DataSourceUpdateSink = null;
            EnableBackgroundUpdating = configuration.EnableBackgroundUpdating;
            EvaluationReasons = configuration.EvaluationReasons;
            Http = (configuration.HttpConfigurationBuilder ?? Components.HttpConfiguration())
                .CreateHttpConfiguration(configuration.MobileKey, environmentReporter.ApplicationInfo);
            InBackground = false;
            ServiceEndpoints = configuration.ServiceEndpoints;
            EnvironmentReporter = environmentReporter;
            DiagnosticDisabler = null;
            DiagnosticStore = null;
            TaskExecutor = new TaskExecutor(
                eventSender,
                AsyncScheduler.ScheduleAction,
                MakeLogger(configuration)
            );
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="toCopy">to use as reference for copying</param>
        private LdClientContext(LdClientContext toCopy)
        {
            MobileKey = toCopy.MobileKey;
            BaseLogger = toCopy.BaseLogger;
            CurrentContext = toCopy.CurrentContext;
            DataSourceUpdateSink = toCopy.DataSourceUpdateSink;
            EnableBackgroundUpdating = toCopy.EnableBackgroundUpdating;
            EvaluationReasons = toCopy.EvaluationReasons;
            Http = toCopy.Http;
            InBackground = toCopy.InBackground;
            ServiceEndpoints = toCopy.ServiceEndpoints;
            EnvironmentReporter = toCopy.EnvironmentReporter;
            DiagnosticDisabler = toCopy.DiagnosticDisabler;
            DiagnosticStore = toCopy.DiagnosticStore;
            TaskExecutor = toCopy.TaskExecutor;
        }

        internal LdClientContext WithLogger(Logger logger) =>
            new LdClientContext(this)
            {
                BaseLogger = logger,
            };

        internal LdClientContext WithContext(Context context) =>
            new LdClientContext(this)
            {
                CurrentContext = context,
            };

        internal LdClientContext WithBackgroundUpdatingEnabled(bool enabled) =>
            new LdClientContext(this)
            {
                EnableBackgroundUpdating = enabled,
            };

        internal LdClientContext WithEvaluationReasons(bool enabled) =>
            new LdClientContext(this)
            {
                EvaluationReasons = enabled,
            };

        internal LdClientContext WithHttpConfiguration(HttpConfiguration configuration) =>
            new LdClientContext(this)
            {
                Http = configuration,
            };

        internal LdClientContext WithInBackground(bool inBackground) =>
            new LdClientContext(this)
            {
                InBackground = inBackground,
            };

        internal LdClientContext WithServiceEndpoints(ServiceEndpoints endpoints) =>
            new LdClientContext(this)
            {
                ServiceEndpoints = endpoints,
            };

        internal LdClientContext WithEnvironmentReporter(IEnvironmentReporter reporter) =>
            new LdClientContext(this)
            {
                EnvironmentReporter = reporter,
            };

        internal LdClientContext WithTaskExecutor(TaskExecutor executor) =>
            new LdClientContext(this)
            {
                TaskExecutor = executor,
            };

        internal LdClientContext WithMobileKey(string mobileKey) =>
            new LdClientContext(this)
            {
                MobileKey = mobileKey,
            };

        internal LdClientContext WithContextAndBackgroundState(Context newCurrentContext, bool newInBackground) =>
            new LdClientContext(this)
            {
                CurrentContext = newCurrentContext,
                InBackground = newInBackground
            };

        internal LdClientContext WithDataSourceUpdateSink(IDataSourceUpdateSink newDataSourceUpdateSink) =>
            new LdClientContext(this)
            {
                DataSourceUpdateSink = newDataSourceUpdateSink
            };

        internal LdClientContext WithDiagnostics(
            IDiagnosticDisabler newDiagnosticDisabler,
            IDiagnosticStore newDiagnosticStore
        ) =>
            new LdClientContext(this)
            {
                DiagnosticDisabler = newDiagnosticDisabler,
                DiagnosticStore = newDiagnosticStore
            };

        internal static Logger MakeLogger(Configuration configuration)
        {
            var logConfig = (configuration.LoggingConfigurationBuilder ?? Components.Logging())
                .CreateLoggingConfiguration();
            var logAdapter = logConfig.LogAdapter ?? Logs.None;
            return logAdapter.Logger(logConfig.BaseLoggerName ?? LogNames.Base);
        }

        internal static IEnvironmentReporter MakeEnvironmentReporter(Configuration configuration)
        {
            var applicationInfoBuilder = configuration.ApplicationInfo;

            var builder = new EnvironmentReporterBuilder();
            if (applicationInfoBuilder != null)
            {
                var applicationInfo = applicationInfoBuilder.Build();

                // If AppInfo is provided by the user, then the Config layer has first priority in the environment reporter.
                builder.SetConfigLayer(new ConfigLayerBuilder().SetAppInfo(applicationInfo).Build());
            }

            // Enable the platform layer if auto env attributes is opted in.
            if (configuration.AutoEnvAttributes)
            {
                // The platform layer has second priority if properties aren't set by the Config layer.
                builder.SetPlatformLayer(PlatformAttributes.Layer);
            }

            // The SDK layer has third priority if properties aren't set by the Platform layer.
            builder.SetSdkLayer(SdkAttributes.Layer);

            return builder.Build();
        }
    }
}
