using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Factory for creating <see cref="IDataSource"/> instances that will be used
    /// by <see cref="CompositeSource"/>.
    /// </summary>
    internal interface ISourceFactory
    {
        /// <summary>
        /// Creates a new <see cref="IDataSource"/> instance, using the provided
        /// <see cref="IDataSourceUpdates"/> sink to push data into the SDK.
        /// </summary>
        /// <param name="updatesSink">the updates sink for the new source</param>
        /// <returns>a new data source instance</returns>
        IDataSource CreateSource(IDataSourceUpdates updatesSink);
    }
}


