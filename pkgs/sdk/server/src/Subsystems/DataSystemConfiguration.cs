using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Configuration for the SDK's data acquisition and storage strategy.
    /// <para>
    /// This class is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is not suitable for production usage. Do not use it. You have been warned.
    /// </para>
    /// </summary>
    internal sealed class DataSystemConfiguration
    {
        // TODO: SDK-1678: Internal until ready for use.

        /// <summary>
        /// Defines the base service URIs used by SDK components.
        /// </summary>
        public ServiceEndpoints ServiceEndpoints { get; }

        /// <summary>
        /// 
        /// </summary>
        public IReadOnlyList<IDataSource> Initializers { get; }

        /// <summary>
        /// 
        /// </summary>
        public IReadOnlyList<IDataSource> Synchronizers { get; }

        /// <summary>
        /// The configured persistent store. This is optional, and if no persistent store is configured, it will be
        /// null.
        /// </summary>
        public IDataStore PersistentStore { get; }

        internal DataSystemConfiguration(ServiceEndpoints serviceEndpoints, IReadOnlyList<IDataSource> initializers,
            IReadOnlyList<IDataSource> synchronizers, IDataStore persistentStore)
        {
        }
    }
}
