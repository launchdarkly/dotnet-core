using System;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Interface for an object that applies actions to a composite data source.
    /// </summary>
    internal interface IActionApplier : IDataSourceUpdatesV2
    {
    }
}
