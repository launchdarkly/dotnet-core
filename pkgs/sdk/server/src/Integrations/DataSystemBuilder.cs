using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Configuration builder for the SDK's data acquisition and storage strategy.
    /// </summary>
    public sealed class DataSystemBuilder
    {
        private readonly List<IComponentConfigurer<IDataSource>> _initializers =
            new List<IComponentConfigurer<IDataSource>>();

        private readonly List<IComponentConfigurer<IDataSource>> _synchronizers =
            new List<IComponentConfigurer<IDataSource>>();

        private IComponentConfigurer<IDataSource> _fdV1FallbackSynchronizer;

        private IComponentConfigurer<IDataStore> _persistentStore;

        private DataSystemConfiguration.DataStoreMode _persistentDataStoreMode;

        private IComponentConfigurer<IOverrideSource> _overrideSource;

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

        /// <summary>
        /// Configures the persistent data store.
        /// </summary>
        /// <param name="persistentStore">the persistent data store</param>
        /// <param name="mode">the mode for the persistent data store</param>
        /// <returns>a reference to the builder</returns>
        /// <remarks>
        /// The SDK will use the persistent data store to store feature flag data.
        /// </remarks>
        /// <seealso cref="DataSystemConfiguration.DataStoreMode"/>
        public DataSystemBuilder PersistentStore(IComponentConfigurer<IDataStore> persistentStore,
            DataSystemConfiguration.DataStoreMode mode)
        {
            _persistentStore = persistentStore;
            _persistentDataStoreMode = mode;
            return this;
        }

        /// <summary>
        /// Configures an override source. Flag overrides are currently experimental and subject to change.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The source supplies flag and segment definitions that take precedence over data received from
        /// LaunchDarkly on a per-key basis. Overrides let an operator force one or more flags to a known
        /// state on a running client, whether or not the client can reach LaunchDarkly. Flags not present
        /// in the override data are unaffected.
        /// </para>
        /// <para>
        /// The override source is not a data source. It has no effect on the client's initialization status
        /// or data source status. Configuring it changes nothing until the source actually supplies an
        /// override. At most one override source can be configured. A later call replaces the earlier one.
        /// </para>
        /// <example>
        /// <code>
        /// var config = Configuration.Builder("my-sdk-key")
        ///   .DataSystem(Components.DataSystem().Default()
        ///     .Overrides(FileOverrides.Source().FilePaths("/etc/launchdarkly/overrides.json")));
        /// </code>
        /// </example>
        /// </remarks>
        /// <param name="overrideSource">the override source, such as <see cref="FileOverrides.Source"/>;
        /// null removes a previously configured source</param>
        /// <returns>a reference to the builder</returns>
        public DataSystemBuilder Overrides(IComponentConfigurer<IOverrideSource> overrideSource)
        {
            _overrideSource = overrideSource;
            return this;
        }

        internal DataSystemConfiguration Build()
        {
            // This function should remain internal.

            return new DataSystemConfiguration(
                _initializers.ToImmutableList(),
                _synchronizers.ToImmutableList(),
                _fdV1FallbackSynchronizer,
                _persistentStore,
                _persistentDataStoreMode,
                _overrideSource);
        }
    }
}
