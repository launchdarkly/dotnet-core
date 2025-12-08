using System;
using LaunchDarkly.Sdk.Server.Internal;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Internal.FDv2DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Internal.Events.DiagnosticConfigProperties;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Contains methods for configuring the polling data source.
    /// <para>
    /// This class is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is not suitable for production usage. Do not use it. You have been warned.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// TODO
    /// </code>
    /// </example>
    /// 
    internal sealed class FDv2PollingDataSourceBuilder : IComponentConfigurer<IDataSource>, IDiagnosticDescription
    {
        // TODO: SDK-1678: Internal until ready for use.

        /// <summary>
        /// The default value for <see cref="PollInterval(TimeSpan)"/>: 30 seconds.
        /// </summary>
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

        internal TimeSpan _pollInterval = DefaultPollInterval;

        /// <summary>
        /// Sets the interval at which the SDK will poll for feature flag updates.
        /// </summary>
        /// <remarks>
        /// The default and minimum value is <see cref="DefaultPollInterval"/>. Values less than this will
        /// be set to the default.
        /// </remarks>
        /// <param name="pollInterval">the polling interval</param>
        /// <returns>the builder</returns>
        public FDv2PollingDataSourceBuilder PollInterval(TimeSpan pollInterval)
        {
            _pollInterval = pollInterval.CompareTo(DefaultPollInterval) >= 0 ? pollInterval : DefaultPollInterval;
            return this;
        }

        // Exposed internally for testing
        internal FDv2PollingDataSourceBuilder PollIntervalNoMinimum(TimeSpan pollInterval)
        {
            _pollInterval = pollInterval;
            return this;
        }

        /// <inheritdoc/>
        public IDataSource Build(LdClientContext context)
        {
            var configuredBaseUri = StandardEndpoints.SelectBaseUri(
                context.ServiceEndpoints, e => e.PollingBaseUri, "Polling",
                context.Logger);

            context.Logger.Warn(
                "You should only disable the streaming API if instructed to do so by LaunchDarkly support");
            var requestor = new FDv2PollingRequestor(context, configuredBaseUri);
            return new FDv2PollingDataSource(
                context,
                context.DataSourceUpdates,
                requestor,
                _pollInterval,
                () => Selector.Empty // TODO: Implement
            );
        }

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.BuildObject()
                .WithPollingProperties(
                    StandardEndpoints.IsCustomUri(context.ServiceEndpoints, e => e.StreamingBaseUri),
                    _pollInterval
                )
                .Add("usingRelayDaemon", false) // this property is specific to the server-side SDK
                .Build();
    }
}
