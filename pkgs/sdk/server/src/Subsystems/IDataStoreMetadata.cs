using System.Collections.Generic;
using System.Collections.Immutable;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// This interface is to allow extending init without a major version.
    /// This interface should be removed in the next major version of the SDK and headers
    /// should be added in the IDataStore interface.
    /// </summary>
    public interface IDataStoreMetadata
    {
        /// <summary>
        /// Overwrites the store's contents with a set of items for each collection.
        /// </summary>
        /// <remarks>
        /// <para>
        /// All previous data should be discarded, regardless of versioning.
        /// </para>
        /// <para>
        /// The update should be done atomically. If it cannot be done atomically, then the store
        /// must first add or update each item in the same order that they are given in the input
        /// data, and then delete any previously stored items that were not in the input data.
        /// </para>
        /// </remarks>
        /// <param name="allData">a list of <see cref="DataStoreTypes.DataKind"/> instances and their
        /// corresponding data sets</param>
        /// <param name="metadata">metadata assciated with the payload</param>
        void InitWithMetadata(DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> allData, DataStoreTypes.InitMetadata metadata);

        /// <summary>
        /// Metadata associated with the data store content.
        /// </summary>
        /// <returns>metadata associated with the store content</returns>
        DataStoreTypes.InitMetadata GetMetadata();
    }
}
