using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    internal interface IReadOnlyStore
    {
        DataStoreTypes.ItemDescriptor? Get(DataStoreTypes.DataKind kind, string key);
        DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor> GetAll(DataStoreTypes.DataKind kind);

        bool Initialized();

        DataStoreTypes.InitMetadata GetMetadata();
    }

    internal interface IFlagChanged
    {
        event EventHandler<FlagChangeEvent> FlagChanged;
    }

    internal interface IDataSystem
    {
        IReadOnlyStore Store { get; }

        Task<bool> Start();
        bool Initialized { get; }

        /// <summary>
        /// The override layer's entries of the kind, without LaunchDarkly data. Empty when no override
        /// source is configured or the layer holds none.
        /// </summary>
        DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor> GetOverrides(DataStoreTypes.DataKind kind);

        /// <summary>
        /// True if the override layer holds an entry of the kind with the key. Always false when no
        /// override source is configured.
        /// </summary>
        bool HasOverride(DataStoreTypes.DataKind kind, string key);

        IFlagChanged FlagChanged { get; }

        IDataSourceStatusProvider DataSourceStatusProvider { get; }
        IDataStoreStatusProvider DataStoreStatusProvider { get; }
    }
}
