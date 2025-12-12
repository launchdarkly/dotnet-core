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
    internal class FDv2DataSystem : IDataSystem, IDisposable
    {
        private readonly WriteThroughStore _store;
        private readonly IDataSource _dataSource;
        private readonly DataSourceUpdatesImpl _dataSourceUpdates;
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
            WriteThroughStore store,
            IDataSource dataSource,
            IDataSourceStatusProvider dataSourceStatusProvider,
            IDataStoreStatusProvider dataStoreStatusProvider,
            DataSourceUpdatesImpl dataStoreUpdates
        )
        {
            _store = store;
            _dataSource = dataSource;
            DataStoreStatusProvider = dataStoreStatusProvider;
            DataSourceStatusProvider = dataSourceStatusProvider;
            FlagChanged = new FlagChangedFacade(dataStoreUpdates);
            _dataSourceUpdates = dataStoreUpdates;
            Store = new ReadonlyStoreFacade(store);
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

            var writeThroughStore = new WriteThroughStore(memoryStore, persistentStore);

            // TODO: When a persistent store is available we monitor it, is this a consistent choice.
            // TODO: Update the responses data store monitoring?
            var dataStoreStatusProvider = new DataStoreStatusProviderImpl(writeThroughStore, dataStoreUpdates);
            var dataSourceUpdates = new DataSourceUpdatesImpl(writeThroughStore, dataStoreStatusProvider,
                clientContext.TaskExecutor, logger, logConfig.LogDataSourceOutageAsErrorAfter);


            var contextWithSelectorSource =
                clientContext.WithSelectorSource(new SelectorSourceFacade(writeThroughStore));
            var compositeDataSource = FDv2DataSource.CreateFDv2DataSource(
                dataSourceUpdates,
                dataSystemConfiguration.Initializers.Select(FactoryWithContext(contextWithSelectorSource)).ToList(),
                dataSystemConfiguration.Synchronizers.Select(FactoryWithContext(contextWithSelectorSource)).ToList(),
                new List<SourceFactory>
                {
                    FactoryWithContext(clientContext)(dataSystemConfiguration.FDv1FallbackSynchronizer)
                }
            );

            var dataSourceStatusProvider = new DataSourceStatusProviderImpl(dataSourceUpdates);

            return new FDv2DataSystem(writeThroughStore, compositeDataSource, dataSourceStatusProvider,
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
                _dataSource.Dispose();
                _store.Dispose();
                _dataSourceUpdates.Dispose();
            }

            _disposed = true;
        }
    }
}
