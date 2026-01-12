using System;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal;
using LaunchDarkly.Sdk.Server.Internal.FDv2DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Internal.Events.DiagnosticConfigProperties;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Contains methods for configuring the polling data source.
    /// <para>
    /// This class is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is in early access. If you want access to this feature please join the EAP. https://launchdarkly.com/docs/sdk/features/data-saving-mode
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// var config = Configuration.Builder("my-sdk-key")
    ///     .DataSystem(Components.DataSystem().Custom()
    ///         // DataSystemComponents.Polling returns an instance to this builder.
    ///         .Initializers(DataSystemComponents.Polling()
    ///           PollInterval(TimeSpan.FromMinutes(10))
    ///         .Synchronizers(DataSystemComponents.Streaming(), DataSystemComponents.Polling())
    ///         .FDv1FallbackSynchronizer(DataSystemComponents.FDv1Polling()));
    /// </code>
    /// </example>
    public sealed class FDv2PollingDataSourceBuilder : IComponentConfigurer<IDataSource>, IDiagnosticDescription
    {
        /// <summary>
        /// The default value for <see cref="PollInterval(TimeSpan)"/>: 30 seconds.
        /// </summary>
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

        internal TimeSpan _pollInterval = DefaultPollInterval;

        private ServiceEndpoints _serviceEndpointsOverride;

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

        /// <summary>
        /// Sets overrides for the service endpoints. In typical usage, the data source will use the commonly defined
        /// service endpoints, but for cases where they need to be controlled at the source level, this method can
        /// be used. This data source will only use the endpoints applicable to it.
        /// </summary>
        /// <param name="serviceEndpointsOverride">the service endpoints to override the base endpoints</param>
        /// <returns>the builder</returns>
        public FDv2PollingDataSourceBuilder ServiceEndpointsOverride(ServiceEndpointsBuilder serviceEndpointsOverride)
        {
            _serviceEndpointsOverride = serviceEndpointsOverride.Build();
            return this;
        }

        /// <inheritdoc/>
        public IDataSource Build(LdClientContext context)
        {
            var endpoints = _serviceEndpointsOverride ?? context.ServiceEndpoints;
            var configuredBaseUri = StandardEndpoints.SelectBaseUri(
                endpoints, e => e.PollingBaseUri, "Polling",
                context.Logger);
            
            var requestor = new FDv2PollingRequestor(context, configuredBaseUri);
            return new FDv2PollingDataSource(
                context,
                context.DataSourceUpdates,
                requestor,
                _pollInterval,
                () => context.SelectorSource?.Selector ?? Selector.Empty
            );
        }

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.BuildObject()
                .WithPollingProperties(
                    StandardEndpoints.IsCustomUri(_serviceEndpointsOverride ?? context.ServiceEndpoints, e => e.StreamingBaseUri),
                    _pollInterval
                )
                .Add("usingRelayDaemon", false) // this property is specific to the server-side SDK
                .Build();
    }
}
