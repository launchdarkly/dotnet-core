using System;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal;
using LaunchDarkly.Sdk.Server.Internal.FDv2DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Internal.Events.DiagnosticConfigProperties;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Contains methods for configuring the streaming data source.
    /// <para>
    /// This class is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is in early access. If you want access to this feature please join the EAP. https://launchdarkly.com/docs/sdk/features/data-saving-mode
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// var config = Configuration.Builder("my-sdk-key")
    ///     .DataSystem(Components.DataSystem().Custom()
    ///         .Initializers(DataSystemComponents.Polling())
    ///         // DataSystemComponents.Streaming returns an instance to this builder.
    ///         .Synchronizers(DataSystemComponents.Streaming()
    ///             .InitialReconnectDelay(TimeSpan.FromSeconds(5)), DataSystemComponents.Polling())
    ///         .FDv1FallbackSynchronizer(DataSystemComponents.FDv1Polling()));
    /// </code>
    /// </example>
    public sealed class FDv2StreamingDataSourceBuilder : IComponentConfigurer<IDataSource>, IDiagnosticDescription
    {
        /// <summary>
        /// The default value for <see cref="InitialReconnectDelay(TimeSpan)"/>: 1000 milliseconds.
        /// </summary>
        public static readonly TimeSpan DefaultInitialReconnectDelay = TimeSpan.FromSeconds(1);

        private TimeSpan _initialReconnectDelay = DefaultInitialReconnectDelay;

        private ServiceEndpoints _serviceEndpointsOverride;

        /// <summary>
        /// Sets the initial reconnect delay for the streaming connection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The streaming service uses a backoff algorithm (with jitter) every time the connection needs
        /// to be reestablished. The delay for the first reconnection will start near this value, and then
        /// increase exponentially for any subsequent connection failures.
        /// </para>
        /// <para>
        /// The default value is <see cref="DefaultInitialReconnectDelay"/>.
        /// </para>
        /// </remarks>
        /// <param name="initialReconnectDelay">the reconnect time base value</param>
        /// <returns>the builder</returns>
        public FDv2StreamingDataSourceBuilder InitialReconnectDelay(TimeSpan initialReconnectDelay)
        {
            _initialReconnectDelay = initialReconnectDelay;
            return this;
        }

        /// <summary>
        /// Sets overrides for the service endpoints. In typical usage, the data source will use the commonly defined
        /// service endpoints, but for cases where they need to be controlled at the source level, this method can
        /// be used. This data source will only use the endpoints applicable to it.
        /// </summary>
        /// <param name="serviceEndpointsOverride">the service endpoints to override the base endpoints</param>
        /// <returns>the builder</returns>
        public FDv2StreamingDataSourceBuilder ServiceEndpointsOverride(ServiceEndpointsBuilder serviceEndpointsOverride)
        {
            _serviceEndpointsOverride = serviceEndpointsOverride.Build();
            return this;
        }

        /// <inheritdoc/>
        public IDataSource Build(LdClientContext context)
        {
            var endpoints = _serviceEndpointsOverride ?? context.ServiceEndpoints;
            var configuredBaseUri = StandardEndpoints.SelectBaseUri(
                endpoints, e => e.StreamingBaseUri, "Streaming",
                context.Logger);
            return new FDv2StreamingDataSource(
                context,
                context.DataSourceUpdates,
                configuredBaseUri,
                _initialReconnectDelay,
                () => context.SelectorSource?.Selector ?? Selector.Empty
            );
        }

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.BuildObject()
                .WithStreamingProperties(
                    StandardEndpoints.IsCustomUri(_serviceEndpointsOverride ?? context.ServiceEndpoints,
                        e => e.StreamingBaseUri),
                    false,
                    _initialReconnectDelay
                )
                .Set("usingRelayDaemon", false)
                .Build();
    }
}
