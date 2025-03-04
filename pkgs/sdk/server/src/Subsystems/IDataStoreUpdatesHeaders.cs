using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// This interface is to allow extending init without a major version.
    /// This interface should be removed in the next major version of the SDK and headers
    /// should be added in the IDataStore interface.
    /// </summary>
    public interface IDataSourceUpdatesHeaders
    {
        /// <summary>
        /// Completely overwrites the current contents of the data store with a set of items for each collection.
        /// </summary>
        /// <param name="allData">a list of <see cref="DataStoreTypes.DataKind"/> instances and their
        /// corresponding data sets</param>
        /// <param name="headers">response headers for the connection</param>
        /// <returns>true if the update succeeded, false if it failed</returns>
        bool InitWithHeaders(DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> allData,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers);
    }
}
