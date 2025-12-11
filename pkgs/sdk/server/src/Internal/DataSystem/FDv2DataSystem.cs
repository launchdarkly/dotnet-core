using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Internal.DataStores;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal class FDv2DataSystem : IDataSystem
    {
        private WriteThroughStore _store;
        private IDataSource _dataSource;
        private DataSourceUpdatesImpl _dataSourceUpdates;
        private bool _disposed;

        #region IDataSystem implementation

        public IReadOnlyStore Store { get; }

        public Task<bool> Start() => _dataSource.Start();

        public bool Initialized => _dataSource.Initialized;

        public IFlagChanged FlagChanged { get; }
        public IDataSourceStatusProvider DataSourceStatusProvider { get; }
        public IDataStoreStatusProvider DataStoreStatusProvider { get; }

        #endregion

        private FDv2DataSystem(
            IDataStore memoryStore,
            IDataStore persistentStore,
            IDataSource dataSource,
            IDataSourceStatusProvider dataSourceStatusProvider,
            IDataStoreStatusProvider dataStoreStatusProvider,
            DataSourceUpdatesImpl dataStoreUpdates
        )
        {
            _store = new WriteThroughStore(memoryStore, persistentStore);
            _dataSource = dataSource;
            DataStoreStatusProvider = dataStoreStatusProvider;
            DataSourceStatusProvider = dataSourceStatusProvider;
            FlagChanged = new FlagChangedFacade(dataStoreUpdates);
            _dataSourceUpdates = dataStoreUpdates;
            Store = new ReadonlyStoreFacade(memoryStore);
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
            var monitoredStore = persistentStore ?? memoryStore;

            // TODO: When a persistent store is available we monitor it, is this a consistent choice.
            // TODO: Update the responses data store monitoring?
            var dataStoreStatusProvider = new DataStoreStatusProviderImpl(monitoredStore, dataStoreUpdates);
            var dataSourceUpdates = new DataSourceUpdatesImpl(monitoredStore, dataStoreStatusProvider,
                clientContext.TaskExecutor, logger, logConfig.LogDataSourceOutageAsErrorAfter);

            var compositeDataSource = FDv2DataSource.CreateFDv2DataSource(
                dataSourceUpdates,
                dataSystemConfiguration.Initializers.Select(FactoryWithContext(clientContext)).ToList(),
                dataSystemConfiguration.Synchronizers.Select(FactoryWithContext(clientContext)).ToList(),
                new List<SourceFactory>
                {
                    FactoryWithContext(clientContext)(dataSystemConfiguration.FDv1FallbackSynchronizer)
                }
            );

            var dataSourceStatusProvider = new DataSourceStatusProviderImpl(dataSourceUpdates);

            return new FDv2DataSystem(memoryStore, persistentStore, compositeDataSource, dataSourceStatusProvider,
                dataStoreStatusProvider, dataSourceUpdates);
        }

        private static Func<IComponentConfigurer<IDataSource>, SourceFactory> FactoryWithContext(
            LdClientContext clientContext)
        {
            return (dataSourceFactory) => ToSourceFactory(dataSourceFactory, clientContext);
        }

        private static SourceFactory ToSourceFactory(IComponentConfigurer<IDataSource> dataSourceFactory,
            LdClientContext clientContext)
        {
            // TODO: WithSelectorSource
            return (sink) => dataSourceFactory.Build(clientContext.WithDataSourceUpdates(sink));
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
                _dataSource.Dispose();
                _store.Dispose();
                _dataSourceUpdates.Dispose();
            }

            _disposed = true;
        }
    }
}
