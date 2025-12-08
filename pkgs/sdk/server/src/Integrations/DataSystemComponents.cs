using System;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Components for use with the data system.
    /// <para>
    /// This class is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is not suitable for production usage. Do not use it. You have been warned.
    /// </para>
    /// </summary>
    internal static class DataSystemComponents
    {
        // TODO: SDK-1678: Internal until ready for use.
        
        /// <summary>
        /// Get a builder for a polling data source.
        /// </summary>
        /// <returns>the polling data source builder</returns>
        public static IComponentConfigurer<IDataSource> Polling()
        {
            return new FDv2PollingDataSourceBuilder();
        }

        /// <summary>
        /// Get a builder for a streaming data source.
        /// </summary>
        /// <returns>the streaming data source builder</returns>
        public static IComponentConfigurer<IDataSource> Streaming()
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
        public static IComponentConfigurer<IDataSource> FDv1Polling()
        {
            return new PollingDataSourceBuilder();
        }
    }
}
