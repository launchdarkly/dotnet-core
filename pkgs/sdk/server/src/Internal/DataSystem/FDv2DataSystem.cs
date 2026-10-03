using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Internal.DataStores;
using LaunchDarkly.Sdk.Server.Internal.FDv2DataSources;
using LaunchDarkly.Sdk.Server.Internal.Overrides;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal class FDv2DataSystem : IDataSystem, IDisposable
    {
        private readonly WriteThroughStore _store;
        private readonly IDataSource _dataSource;
        private readonly DataSourceUpdatesImpl _dataSourceUpdates;
        // The override source, its layer, and its sink are null when no override source is configured.
        private readonly IOverrideSource _overrideSource;
        private readonly OverrideLayer _overrideLayer;
        private readonly OverrideSink _overrideSink;
        private bool _disposed;

        #region IDataSystem implementation

        public IReadOnlyStore Store { get; }

        public Task<bool> Start()
        {
            // The override source starts before the data sources, and its initial load completes
            // synchronously, so overrides present when the client starts are in effect before the
            // client evaluates anything. The source has no effect on initialization. A source that
            // fails to start fails the client constructor; the data system is disposed first, so
            // nothing it created is left running.
            try
            {
                _overrideSource?.Start(_overrideSink);
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
            return _dataSource.Start();
        }

        public bool Initialized => _dataSource.Initialized;

        public bool OverridesConfigured => _overrideSource != null;

        // No entry has a null key, so a null key is not held. The evaluation of a null key then
        // takes the same path as without an override source.
        public bool HasOverride(DataStoreTypes.DataKind kind, string key) =>
            key != null && _overrideLayer != null && _overrideLayer.Get(kind, key).HasValue;

        public IFlagChanged FlagChanged { get; }
        public IDataSourceStatusProvider DataSourceStatusProvider { get; }
        public IDataStoreStatusProvider DataStoreStatusProvider { get; }

        #endregion

        private FDv2DataSystem(
            WriteThroughStore store,
            IReadOnlyStore readOnlyStore,
            IDataSource dataSource,
            IDataSourceStatusProvider dataSourceStatusProvider,
            IDataStoreStatusProvider dataStoreStatusProvider,
            DataSourceUpdatesImpl dataStoreUpdates,
            IOverrideSource overrideSource,
            OverrideLayer overrideLayer,
            OverrideSink overrideSink
        )
        {
            _store = store;
            _dataSource = dataSource;
            DataStoreStatusProvider = dataStoreStatusProvider;
            DataSourceStatusProvider = dataSourceStatusProvider;
            FlagChanged = new FlagChangedFacade(dataStoreUpdates);
            _dataSourceUpdates = dataStoreUpdates;
            Store = readOnlyStore;
            _overrideSource = overrideSource;
            _overrideLayer = overrideLayer;
            _overrideSink = overrideSink;
        }

        public static FDv2DataSystem Create(Logger logger, Configuration configuration, LdClientContext clientContext,
            LoggingConfiguration logConfig)
        {
            var dataSystemConfiguration = configuration.DataSystem.Build();
            var dataStoreUpdates =
                new DataStoreUpdatesImpl(clientContext.TaskExecutor, logger.SubLogger(LogNames.DataStoreSubLog));
            var memoryStore = new InMemoryDataStore();

            var persistentStore =
                dataSystemConfiguration.PersistentStore?.Build(clientContext.WithDataStoreUpdates(dataStoreUpdates));

            // Configure persistent store to sync from memory store during recovery (ReadWrite mode only)
            if (persistentStore != null &&
                dataSystemConfiguration.PersistentDataStoreMode == DataSystemConfiguration.DataStoreMode.ReadWrite)
            {
                if (persistentStore is ISettableCache externalSourceSupport)
                {
                    externalSourceSupport.SetCacheExporter(memoryStore);
                }
            }

            var writeThroughStore = new WriteThroughStore(memoryStore, persistentStore,
                dataSystemConfiguration.PersistentDataStoreMode);
            
            var dataStoreStatusProvider = new DataStoreStatusProviderImpl(writeThroughStore, dataStoreUpdates);
            var dataSourceUpdates = new DataSourceUpdatesImpl(writeThroughStore, dataStoreStatusProvider,
                clientContext.TaskExecutor, logger, logConfig.LogDataSourceOutageAsErrorAfter);

            var contextWithSelectorSource =
                clientContext.WithSelectorSource(new SelectorSourceFacade(writeThroughStore));
            // FDv1 fallback synchronizer is optional; only build a list entry when one is
            // configured. An always-present list entry that captured a null configurer would
            // throw NRE the moment the action applier advanced to the FDv1 fallback entry.
            var fdv1FallbackFactories = dataSystemConfiguration.FDv1FallbackSynchronizer == null
                ? new List<SourceFactory>()
                : new List<SourceFactory>
                {
                    FactoryWithContext(clientContext)(dataSystemConfiguration.FDv1FallbackSynchronizer)
                };
            var compositeDataSource = configuration.Offline ? Components.ExternalUpdatesOnly.Build(contextWithSelectorSource) : FDv2DataSource.CreateFDv2DataSource(
                dataSourceUpdates,
                dataSystemConfiguration.Initializers.Select(FactoryWithContext(contextWithSelectorSource)).ToList(),
                dataSystemConfiguration.Synchronizers.Select(FactoryWithContext(contextWithSelectorSource)).ToList(),
                fdv1FallbackFactories,
                logger
            );

            var dataSourceStatusProvider = new DataSourceStatusProviderImpl(dataSourceUpdates);

            // The override layer sits at the store read boundary. Every read the client performs
            // goes through the overlay, so evaluation, prerequisite and segment resolution, and
            // the all-flags state see override entries in preference to LaunchDarkly data. The
            // overlay never touches the data system's own writes or its initialization status.
            IReadOnlyStore readOnlyStore = new ReadonlyStoreFacade(writeThroughStore);
            IOverrideSource overrideSource = null;
            OverrideLayer overrideLayer = null;
            OverrideSink overrideSink = null;
            if (dataSystemConfiguration.OverrideSource != null && !configuration.Offline)
            {
                // A configuration error in the source is reported like any other invalid component
                // configuration: the exception propagates out of the client constructor. The
                // components created so far are disposed first, so a failed constructor leaves
                // nothing running.
                try
                {
                    overrideSource = dataSystemConfiguration.OverrideSource.Build(clientContext);
                }
                catch (Exception)
                {
                    compositeDataSource.Dispose();
                    dataSourceUpdates.Dispose();
                    writeThroughStore.Dispose();
                    throw;
                }
                overrideLayer = new OverrideLayer();
                overrideSink = new OverrideSink(overrideLayer, readOnlyStore,
                    dataSourceUpdates.SendFlagChangeEvents, dataSourceUpdates.HasFlagChangeListeners,
                    logger.SubLogger(LogNames.OverridesSubLog));
                readOnlyStore = new OverrideOverlay(readOnlyStore, overrideLayer);
            }

            return new FDv2DataSystem(writeThroughStore, readOnlyStore, compositeDataSource, dataSourceStatusProvider,
                dataStoreStatusProvider, dataSourceUpdates, overrideSource, overrideLayer, overrideSink);
        }

        private static Func<IComponentConfigurer<IDataSource>, SourceFactory> FactoryWithContext(
            LdClientContext clientContext)
        {
            return (dataSourceFactory) => ToSourceFactory(dataSourceFactory, clientContext);
        }

        private static SourceFactory ToSourceFactory(IComponentConfigurer<IDataSource> dataSourceFactory,
            LdClientContext clientContext)
        {
            return (sink) =>
                dataSourceFactory.Build(clientContext.WithDataSourceUpdates(new DataSourceUpdatesV2ToV1Adapter(sink)));
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _overrideSource?.Dispose();
                _dataSource.Dispose();
                _store.Dispose();
                _dataSourceUpdates.Dispose();
            }

            _disposed = true;
        }
    }
}
