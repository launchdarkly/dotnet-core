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
        /// True if the data system was built with an override source. When true, the store applies the
        /// override layer, and the client consults it before its not-initialized short-circuit.
        /// </summary>
        bool OverridesConfigured { get; }

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
