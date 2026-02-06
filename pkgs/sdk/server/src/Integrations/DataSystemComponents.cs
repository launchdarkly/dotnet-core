using System;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Components for use with the data system.
    /// <para>
    /// This class is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is in early access. If you want access to this feature please join the EAP. https://launchdarkly.com/docs/sdk/features/data-saving-mode
    /// </para>
    /// </summary>
    public static class DataSystemComponents
    {
        
        /// <summary>
        /// Get a builder for a polling data source.
        /// </summary>
        /// <returns>the polling data source builder</returns>
        public static FDv2PollingDataSourceBuilder Polling()
        {
            return new FDv2PollingDataSourceBuilder();
        }

        /// <summary>
        /// Get a builder for a streaming data source.
        /// </summary>
        /// <returns>the streaming data source builder</returns>
        public static FDv2StreamingDataSourceBuilder Streaming()
        {
            return new FDv2StreamingDataSourceBuilder();
        }

        /// <summary>
        /// Get a builder for a FDv1 compatible polling data source.
        /// <remarks>
        /// This is intended for use as a fallback.
        /// </remarks>
        /// </summary>
        /// <returns>the FDv1 compatible polling data source.</returns>
        public static PollingDataSourceBuilder FDv1Polling()
        {
            return new PollingDataSourceBuilder();
        }
    }
}
