using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Configuration builder for the SDK's data acquisition and storage strategy.
    /// <para>
    /// This class is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is not suitable for production usage. Do not use it. You have been warned.
    /// </para>
    /// </summary>
    internal sealed class DataSystemBuilder
    {
        // TODO: SDK-1678: Internal until ready for use.

        private readonly List<IComponentConfigurer<IDataSource>> _initializers =
            new List<IComponentConfigurer<IDataSource>>();

        private readonly List<IComponentConfigurer<IDataSource>> _synchronizers =
            new List<IComponentConfigurer<IDataSource>>();

        private IComponentConfigurer<IDataSource> _fdV1FallbackSynchronizer;

        private IComponentConfigurer<IDataStore> _persistentStore;

        private DataSystemConfiguration.DataStoreMode _persistentDataStoreMode;
        
        /// <summary>
        /// Add one or more initializers to the builder.
        /// To replace initializers, please refer to <see cref="DataSystemBuilder.ReplaceInitializers"/>.
        /// </summary>
        /// <param name="initializers">the initializers to add</param>
        /// <returns>a reference to the builder</returns>
        public DataSystemBuilder Initializers(params IComponentConfigurer<IDataSource>[] initializers)
        {
            _initializers.AddRange(initializers);
            return this;
        }

        /// <summary>
        /// Replaces any existing initializers with the given initializers.
        /// To add initializers, please refer to <see cref="Initializers"/>.
        /// </summary>
        /// <param name="initializers">the initializers to replace the current initializers with</param>
        /// <returns>a reference to this builder</returns>
        public DataSystemBuilder ReplaceInitializers(params IComponentConfigurer<IDataSource>[] initializers)
        {
            _initializers.Clear();
            _initializers.AddRange(initializers);
            return this;
        }

        /// <summary>
        /// Add one or more synchronizers to the builder.
        /// To replace synchronizers, please refer to <see cref="DataSystemBuilder.ReplaceSynchronizers"/>.
        /// </summary>
        /// <param name="synchronizers">the synchronizers to add</param>
        /// <returns>a reference to the builder</returns>
        public DataSystemBuilder Synchronizers(params IComponentConfigurer<IDataSource>[] synchronizers)
        {
            _synchronizers.AddRange(synchronizers);
            return this;
        }

        /// <summary>
        /// Replaces any existing synchronizers with the given synchronizers.
        /// To add synchronizers, please refer to <see cref="Synchronizers"/>.
        /// </summary>
        /// <param name="synchronizers">the synchronizers to replace the current synchronizers with</param>
        /// <returns>a reference to this builder</returns>
        public DataSystemBuilder ReplaceSynchronizers(params IComponentConfigurer<IDataSource>[] synchronizers)
        {
            _synchronizers.Clear();
            _synchronizers.AddRange(synchronizers);
            return this;
        }

        /// <summary>
        /// Configured the FDv1 fallback synchronizer.
        /// <remarks>LaunchDarkly can instruct the SDK to fall back to this synchronizer.</remarks>
        /// </summary>
        /// <param name="fdv1FallbackSynchronizer">the FDv1 fallback synchronizer</param>
        /// <returns>a reference to the builder</returns>
        public DataSystemBuilder FDv1FallbackSynchronizer(IComponentConfigurer<IDataSource> fdv1FallbackSynchronizer)
        {
            _fdV1FallbackSynchronizer = fdv1FallbackSynchronizer;
            return this;
        }

        public DataSystemBuilder PersistentStore(IComponentConfigurer<IDataStore> persistentStore,
            DataSystemConfiguration.DataStoreMode mode)
        {
            _persistentStore = persistentStore;
            _persistentDataStoreMode = mode;
            return this;
        }

        internal DataSystemConfiguration Build()
        {
            // This function should remain internal.

            return new DataSystemConfiguration(
                _initializers,
                _synchronizers,
                _fdV1FallbackSynchronizer,
                _persistentStore,
                _persistentDataStoreMode);
        }
    }
}
