using System;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Interface that can observe changes coming from a composite data source.
    /// </summary>
    internal interface IDataSourceObserver
    {
        /// <param name="allData">full data set from the data source</param>
        /// <returns>true if the initialization succeeded, false if it failed</returns>
        void Init(FullDataSet<ItemDescriptor> allData);

        /// <param name="kind">the kind of data</param>
        /// <param name="key">the key of the data</param>
        /// <param name="item">the data item</param>
        void Upsert(DataKind kind, string key, ItemDescriptor item);

        /// <param name="newState">the data source state</param>
        /// <param name="newError">information about a new error, if any</param>
        void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError);
    }
}
