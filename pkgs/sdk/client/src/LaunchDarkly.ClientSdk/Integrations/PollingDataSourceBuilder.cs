using System;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Client.Internal.DataSources;
using LaunchDarkly.Sdk.Client.Subsystems;

using static LaunchDarkly.Sdk.Internal.Events.DiagnosticConfigProperties;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    /// <summary>
    /// Contains methods for configuring the polling data source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Polling is not the default behavior; by default, the SDK uses a streaming connection to receive feature flag
    /// data from LaunchDarkly. In polling mode, the SDK instead makes a new HTTP request to LaunchDarkly at regular
    /// intervals. HTTP caching allows it to avoid redundantly downloading data if there have been no changes, but
    /// polling is still less efficient than streaming and should only be used on the advice of LaunchDarkly support.
    /// </para>
    /// <para>
    /// To use polling mode, create a builder with <see cref="Components.PollingDataSource"/>, change its properties
    /// with the methods of this class, and pass it to <see cref="ConfigurationBuilder.DataSource(IComponentConfigurer{IDataSource})"/>.
    /// </para>
    /// <para>
    /// Setting <see cref="ConfigurationBuilder.Offline(bool)"/> to <see langword="true"/> will supersede this
    /// setting and completely disable network requests.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    ///     var config = Configuration.Builder(sdkKey)
    ///         .DataSource(Components.PollingDataSource()
    ///             .PollInterval(TimeSpan.FromSeconds(45)))
    ///         .Build();
    /// </code>
    /// </example>
    public sealed class PollingDataSourceBuilder : IComponentConfigurer<IDataSource>, IDiagnosticDescription
    {
        /// <summary>
        /// The default value for <see cref="PollInterval(TimeSpan)"/>: 5 minutes.
        /// </summary>
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(5);

        internal TimeSpan _backgroundPollInterval = Configuration.DefaultBackgroundPollInterval;
        internal TimeSpan _pollInterval = DefaultPollInterval;

        /// <summary>
        /// Sets the interval between feature flag updates when the application is running in the background.
        /// </summary>
        /// <remarks>
        /// This is only relevant on mobile platforms. The default is <see cref="Configuration.DefaultBackgroundPollInterval"/>;
        /// the minimum is <see cref="Configuration.MinimumBackgroundPollInterval"/>.
        /// </remarks>
        /// <param name="backgroundPollInterval">the background polling interval</param>
        /// <returns>the same builder</returns>
        /// <seealso cref="ConfigurationBuilder.EnableBackgroundUpdating(bool)"/>
        public PollingDataSourceBuilder BackgroundPollInterval(TimeSpan backgroundPollInterval)
        {
            _backgroundPollInterval = (backgroundPollInterval < Configuration.MinimumBackgroundPollInterval) ?
                Configuration.MinimumBackgroundPollInterval : backgroundPollInterval;
            return this;
        }

        /// <summary>
        /// Sets the interval at which the SDK will poll for feature flag updates.
        /// </summary>
        /// <remarks>
        /// The default and minimum value is <see cref="DefaultPollInterval"/>. Values less than this will
        /// be set to the default.
        /// </remarks>
        /// <param name="pollInterval">the polling interval</param>
        /// <returns>the builder</returns>
        public PollingDataSourceBuilder PollInterval(TimeSpan pollInterval)
        {
            _pollInterval = (pollInterval < DefaultPollInterval) ?
                DefaultPollInterval :
                pollInterval;
            return this;
        }

        // Exposed internally for testing
        internal PollingDataSourceBuilder PollIntervalNoMinimum(TimeSpan pollInterval)
        {
            _pollInterval = pollInterval;
            return this;
        }

        /// <inheritdoc/>
        public IDataSource Build(LdClientContext clientContext)
        {
            if (!clientContext.InBackground)
            {
                clientContext.BaseLogger.Warn("You should only disable the streaming API if instructed to do so by LaunchDarkly support");
            }
            var baseUri = StandardEndpoints.SelectBaseUri(
                clientContext.ServiceEndpoints,
                e => e.PollingBaseUri,
                "Polling",
                clientContext.BaseLogger
                );

            var logger = clientContext.BaseLogger.SubLogger(LogNames.DataSourceSubLog);
            var requestor = new FeatureFlagRequestor(
                baseUri,
                clientContext.CurrentContext,
                clientContext.EvaluationReasons,
                clientContext.Http,
                logger
                );

            return new PollingDataSource(
                clientContext.DataSourceUpdateSink,
                clientContext.CurrentContext,
                requestor,
                _pollInterval,
                TimeSpan.Zero,
                clientContext.TaskExecutor,
                logger
                );
        }

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.BuildObject()
                .WithPollingProperties(
                    StandardEndpoints.IsCustomUri(context.ServiceEndpoints, e => e.PollingBaseUri),
                    _pollInterval
                )
                .Add("backgroundPollingIntervalMillis", _backgroundPollInterval.TotalMilliseconds)
                .Build();
    }
}
