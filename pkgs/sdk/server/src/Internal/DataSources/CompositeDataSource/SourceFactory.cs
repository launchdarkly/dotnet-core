using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// A function type that creates a new <see cref="IDataSource"/> instance.
    /// </summary>
    /// <param name="updatesSink">the updates sink for the new source</param>
    /// <returns>a new <see cref="IDataSource"/> instance</returns>
    internal delegate IDataSource SourceFactory(IDataSourceUpdatesV2 updatesSink);
}

