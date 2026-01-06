using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Optional interface for data stores that can export their entire contents.
    /// </summary>
    /// <remarks>
    /// This interface is used to enable recovery scenarios where a persistent store
    /// needs to be re-synchronized from an in-memory cache. Not all data stores need
    /// to implement this interface.
    /// </remarks>
    public interface IDataStoreExporter
    {
        /// <summary>
        /// Exports all data from the store across all known DataKinds.
        /// </summary>
        /// <returns>
        /// A FullDataSet containing all items in the store. The data is a snapshot
        /// taken at the time of the call and may be stale immediately after return.
        /// </returns>
        FullDataSet<ItemDescriptor> ExportAllData();
    }
}
