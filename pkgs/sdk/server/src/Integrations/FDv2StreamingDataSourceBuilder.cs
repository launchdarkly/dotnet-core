using System;
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
    /// It is not suitable for production usage. Do not use it. You have been warned.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// TODO: Write.
    /// </code>
    /// </example>
    internal sealed class FDv2StreamingDataSourceBuilder : IComponentConfigurer<IDataSource>, IDiagnosticDescription
    {
        // TODO: SDK-1678: Internal until ready for use.

        /// <summary>
        /// The default value for <see cref="InitialReconnectDelay(TimeSpan)"/>: 1000 milliseconds.
        /// </summary>
        public static readonly TimeSpan DefaultInitialReconnectDelay = TimeSpan.FromSeconds(1);

        private TimeSpan _initialReconnectDelay = DefaultInitialReconnectDelay;

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

        /// <inheritdoc/>
        public IDataSource Build(LdClientContext context)
        {
            var configuredBaseUri = StandardEndpoints.SelectBaseUri(
                context.ServiceEndpoints, e => e.StreamingBaseUri, "Streaming",
                context.Logger);
            return new FDv2StreamingDataSource(
                context,
                context.DataSourceUpdates,
                configuredBaseUri,
                _initialReconnectDelay,
                () => Selector.Empty // TODO: Implement.
            );
        }

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.BuildObject()
                .WithStreamingProperties(
                    StandardEndpoints.IsCustomUri(context.ServiceEndpoints, e => e.StreamingBaseUri),
                    false,
                    _initialReconnectDelay
                )
                .Set("usingRelayDaemon", false)
                .Build();
    }
}
