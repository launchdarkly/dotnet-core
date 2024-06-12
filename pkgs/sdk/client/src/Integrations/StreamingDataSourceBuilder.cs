using System;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Client.Internal.DataSources;
using LaunchDarkly.Sdk.Client.Subsystems;

using static LaunchDarkly.Sdk.Internal.Events.DiagnosticConfigProperties;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    /// <summary>
    /// Contains methods for configuring the streaming data source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default, the SDK uses a streaming connection to receive feature flag data from LaunchDarkly. If you want
    /// to customize the behavior of the connection, create a builder with <see cref="Components.StreamingDataSource"/>,
    /// change its properties with the methods of this class, and pass it to
    /// <see cref="ConfigurationBuilder.DataSource(IComponentConfigurer{IDataSource})"/>.
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
    public sealed class StreamingDataSourceBuilder : IComponentConfigurer<IDataSource>, IDiagnosticDescription
    {
        /// <summary>
        /// The default value for <see cref="InitialReconnectDelay(TimeSpan)"/>: 1000 milliseconds.
        /// </summary>
        public static readonly TimeSpan DefaultInitialReconnectDelay = TimeSpan.FromSeconds(1);

        internal TimeSpan _backgroundPollInterval = Configuration.DefaultBackgroundPollInterval;
        internal TimeSpan _initialReconnectDelay = DefaultInitialReconnectDelay;

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
        public StreamingDataSourceBuilder BackgroundPollInterval(TimeSpan backgroundPollInterval)
        {
            _backgroundPollInterval = (backgroundPollInterval < Configuration.MinimumBackgroundPollInterval) ?
                Configuration.MinimumBackgroundPollInterval : backgroundPollInterval;
            return this;
        }

        internal StreamingDataSourceBuilder BackgroundPollingIntervalWithoutMinimum(TimeSpan backgroundPollInterval)
        {
            _backgroundPollInterval = backgroundPollInterval;
            return this;
        }

        /// <summary>
        /// Sets the initial reconnect delay for the streaming connection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The streaming service uses a backoff algorithm (with jitter) every time the connection needs
        /// to be reestablished.The delay for the first reconnection will start near this value, and then
        /// increase exponentially for any subsequent connection failures.
        /// </para>
        /// <para>
        /// The default value is <see cref="DefaultInitialReconnectDelay"/>.
        /// </para>
        /// </remarks>
        /// <param name="initialReconnectDelay">the reconnect time base value</param>
        /// <returns>the builder</returns>
        public StreamingDataSourceBuilder InitialReconnectDelay(TimeSpan initialReconnectDelay)
        {
            _initialReconnectDelay = initialReconnectDelay;
            return this;
        }

        /// <inheritdoc/>
        public IDataSource Build(LdClientContext clientContext)
        {
            var baseUri = StandardEndpoints.SelectBaseUri(
                clientContext.ServiceEndpoints,
                e => e.StreamingBaseUri,
                "Streaming",
                clientContext.BaseLogger
                );
            var pollingBaseUri = StandardEndpoints.SelectBaseUri(
                clientContext.ServiceEndpoints,
                e => e.PollingBaseUri,
                "Polling",
                clientContext.BaseLogger
                );

            if (clientContext.InBackground)
            {
                // When in the background, always use polling instead of streaming
                return new PollingDataSourceBuilder()
                    .BackgroundPollInterval(_backgroundPollInterval)
                    .Build(clientContext);
            }

            var logger = clientContext.BaseLogger.SubLogger(LogNames.DataSourceSubLog);
            var requestor = new FeatureFlagRequestor(
                pollingBaseUri,
                clientContext.CurrentContext,
                clientContext.EvaluationReasons,
                clientContext.Http,
                logger
                );

            return new StreamingDataSource(
                clientContext.DataSourceUpdateSink,
                clientContext.CurrentContext,
                baseUri,
                clientContext.EvaluationReasons,
                _initialReconnectDelay,
                requestor,
                clientContext.Http,
                logger,
                clientContext.DiagnosticStore
                );
        }

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.BuildObject()
                .WithStreamingProperties(
                    StandardEndpoints.IsCustomUri(context.ServiceEndpoints, e => e.PollingBaseUri),
                    StandardEndpoints.IsCustomUri(context.ServiceEndpoints, e => e.StreamingBaseUri),
                    _initialReconnectDelay
                )
                .Add("backgroundPollingIntervalMillis", _backgroundPollInterval.TotalMilliseconds)
                .Build();
    }
}
