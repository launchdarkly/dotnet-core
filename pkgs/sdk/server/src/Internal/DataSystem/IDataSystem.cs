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

        IFlagChanged FlagChanged { get; }

        IDataSourceStatusProvider DataSourceStatusProvider { get; }
        IDataStoreStatusProvider DataStoreStatusProvider { get; }
    }
}
