using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Internal.DataStores;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal class FDv1DataSystem : IDataSystem

    {
        private readonly IDataSource _dataSource;

        #region Testing access to the internal components
        internal class TestingAccess
        {
            internal IDataSource DataSource { get; }

            public TestingAccess(IDataSource dataSource)
            {
                DataSource = dataSource;
            }
        }
        
        public TestingAccess Testing { get; }
        
        #endregion

        #region IDataSystem implementation

        public IReadOnlyStore Store { get; }

        public Task<bool> Start()
        {
            return _dataSource.Start();
        }

        public bool Initialized => _dataSource.Initialized;

        public IFlagChanged FlagChanged { get; }
        public IDataSourceStatusProvider DataSourceStatusProvider { get; }
        public IDataStoreStatusProvider DataStoreStatusProvider { get; }

        #endregion

        private FDv1DataSystem(
            IDataStore store,
            IDataStoreStatusProvider dataStoreStatusProvider,
            IDataSourceStatusProvider dataSourceStatusProvider,
            DataSourceUpdatesImpl dataSourceUpdates,
            IDataSource dataSource
        )
        {
            DataSourceStatusProvider = dataSourceStatusProvider;
            DataStoreStatusProvider = dataStoreStatusProvider;
            Store = new ReadonlyStoreFacade(store);
            FlagChanged = new FlagChangedFacade(dataSourceUpdates);
            _dataSource = dataSource;
            Testing = new TestingAccess(dataSource);
        }

        public static FDv1DataSystem Create(Logger logger, Configuration configuration, LdClientContext clientContext,
            LoggingConfiguration logConfig)
        {
            var dataStoreUpdates =
                new DataStoreUpdatesImpl(clientContext.TaskExecutor, logger.SubLogger(LogNames.DataStoreSubLog));

            var dataStore = (configuration.DataStore ?? Components.InMemoryDataStore)
                .Build(clientContext.WithDataStoreUpdates(dataStoreUpdates));

            var dataStoreStatusProvider = new DataStoreStatusProviderImpl(dataStore, dataStoreUpdates);

            var dataSourceUpdates = new DataSourceUpdatesImpl(dataStore, dataStoreStatusProvider,
                clientContext.TaskExecutor, logger, logConfig.LogDataSourceOutageAsErrorAfter);

            var dataSourceFactory =
                configuration.Offline
                    ? Components.ExternalUpdatesOnly
                    : (configuration.DataSource ?? Components.StreamingDataSource());
            var dataSource = dataSourceFactory.Build(clientContext.WithDataSourceUpdates(dataSourceUpdates));
            var dataSourceStatusProvider = new DataSourceStatusProviderImpl(dataSourceUpdates);

            return new FDv1DataSystem(
                dataStore,
                dataStoreStatusProvider,
                dataSourceStatusProvider,
                dataSourceUpdates,
                dataSource
            );
        }
    }
}
