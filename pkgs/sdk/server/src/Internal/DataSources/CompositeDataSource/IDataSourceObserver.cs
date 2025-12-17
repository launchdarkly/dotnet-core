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
        void Apply(ChangeSet<ItemDescriptor> changeSet, bool exhausted);

        /// <param name="newState">the data source state</param>
        /// <param name="newError">information about a new error, if any</param>
        void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError);
    }
}
