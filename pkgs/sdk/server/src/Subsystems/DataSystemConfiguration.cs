using System.Collections.Generic;

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
        /// The persistent data store mode.
        /// </summary>
        /// <remarks>
        /// This enum can be extended without a major version. Code should provide this value in configuration,
        /// but it should not use the enum itself, for example, in a switch-case.
        /// </remarks>
        public enum DataStoreMode
        {
            /// <summary>
            ///  The data system will only read from the persistent store.
            /// </summary>
            ReadOnly,

            /// <summary>
            /// The data system can read from, and write to, the persistent store.
            /// </summary>
            ReadWrite,
        }

        /// <summary>
        /// A list of factories for creating data sources for initialization.
        /// </summary>
        public IReadOnlyList<IComponentConfigurer<IDataSource>> Initializers { get; }

        /// <summary>
        /// A list of factories for creating data sources for synchronization.
        /// </summary>
        public IReadOnlyList<IComponentConfigurer<IDataSource>> Synchronizers { get; }

        /// <summary>
        /// A synchronizer to fall back to when FDv1 fallback has been requested.
        /// </summary>
        public IComponentConfigurer<IDataSource> FDv1FallbackSynchronizer { get; }

        /// <summary>
        /// An optional factory for creating a persistent data store. This is optional, and if no persistent store is configured, it will be
        /// null.
        /// </summary>
        /// <remarks>
        /// The persistent store itself will implement <see cref="IPersistentDataStore"/> or
        /// <see cref="IPersistentDataStoreAsync"/>, but we expect that to be wrapped by a factory which can
        /// operates at the <see cref="IDataStore"/> level.
        /// </remarks>
        public IComponentConfigurer<IDataStore> PersistentStore { get; }
        
        /// <summary>
        /// The mode of operation for the persistent data store.
        /// </summary>
        public DataStoreMode PersistentDataStoreMode { get; }

        internal DataSystemConfiguration(
            IReadOnlyList<IComponentConfigurer<IDataSource>> initializers,
            IReadOnlyList<IComponentConfigurer<IDataSource>> synchronizers,
            IComponentConfigurer<IDataSource> fDv1FallbackSynchronizer,
            IComponentConfigurer<IDataStore> persistentStore,
            DataStoreMode persistentDataStoreMode)
        {
            Initializers = initializers;
            Synchronizers = synchronizers;
            FDv1FallbackSynchronizer = fDv1FallbackSynchronizer;
            PersistentStore = persistentStore;
            PersistentDataStoreMode = persistentDataStoreMode;
        }
    }
}
