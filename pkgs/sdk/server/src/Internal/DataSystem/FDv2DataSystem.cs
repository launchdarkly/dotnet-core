using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataStores;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal class FDv2DataSystem : IDataSystem
    {
        private IDataStore _memoryStore;
        private IDataStore _persistentStore;
        private IReadOnlyList<IComponentConfigurer<IDataSource>> _initializerFactories;
        private IReadOnlyList<IComponentConfigurer<IDataSource>> _synchronizerFactories;
        private IComponentConfigurer<IDataSource> _fdv1FallbackSynchronizerFactory;

        #region IDataSystem implementation

        public IReadOnlyStore Store { get; }

        public Task<bool> Start()
        {
            throw new NotImplementedException();
        }

        public bool Initialized { get; private set; }

        public IFlagChanged FlagChanged { get; }
        public IDataSourceStatusProvider DataSourceStatusProvider { get; }
        public IDataStoreStatusProvider DataStoreStatusProvider { get; }

        #endregion

        /// <summary>
        /// Construct an instance of the FDv2DataSystem
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="memoryStore"></param>
        public FDv2DataSystem(DataSystemConfiguration configuration, IDataStore memoryStore = null)
        {
            _memoryStore = memoryStore ?? new InMemoryDataStore();
            _initializerFactories = configuration.Initializers;
            _synchronizerFactories = configuration.Synchronizers;
            _fdv1FallbackSynchronizerFactory = configuration.FDv1FallbackSynchronizer;
        }
    }
}
