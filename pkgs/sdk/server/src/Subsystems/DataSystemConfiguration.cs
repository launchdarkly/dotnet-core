using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Configuration for the SDK's data acquisition and storage strategy.
    /// </summary>
    public sealed class DataSystemConfiguration
    {
        /// <summary>
        /// Defines the base service URIs used by SDK components.
        /// </summary>
        public ServiceEndpoints ServiceEndpoints { get; }
        
        // TODO: Do we want a different type for initializers/synchronizes?
        // We could probably decompose polling into a one-shot and then use that in a synchronizer.
        // public IReadOnlyList<IInitializer> Initializers { get; }
        
        // TODO: Currently streaming/polling are implemented as a data source, but we could
        // easily put the required methods into new interface.
        // public IReadonlyList<ISynchronizer> Synchronizers { get; }
        
        /// <summary>
        /// The configured persistent store. This is optional, and if no persistent store is configured, it will be
        /// null.
        /// </summary>
        public IDataStore PersistentStore { get;  }
        
    }
}
