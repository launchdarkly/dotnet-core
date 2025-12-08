namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Interface for data sources that can be used to initialize the LaunchDarkly SDK with an initial payload.
    /// </summary>
    public interface IInitializer
    {
        /// <summary>
        /// Fetch returns a changeset. In order for initialization to complete the ChangeSet must be a full transfer.
        /// Initializers which only support "cached" data, such a a file data source, should include an empty
        /// selector in their ChangeSet.
        /// </summary>
        /// <returns>A ChangeSet</returns>
        DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> Fetch();
    }
}
