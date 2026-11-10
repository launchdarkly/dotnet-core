namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    /// <summary>
    /// Implemented by a data source configurer that states whether the FDv2 data system may write the
    /// source's data to a persistent store.
    /// </summary>
    /// <remarks>
    /// The data system reads this when it builds the source, and applies it to every full data set and
    /// every upsert that the source delivers. A configurer that does not implement this interface
    /// produces data that is persisted. The FDv1 data system does not use this: there, the data store
    /// receives all data.
    /// </remarks>
    internal interface IDataSourcePersistence
    {
        /// <summary>
        /// True if the data system may write the data from the sources built by this configurer to a
        /// persistent store.
        /// </summary>
        bool ShouldPersist { get; }
    }
}
