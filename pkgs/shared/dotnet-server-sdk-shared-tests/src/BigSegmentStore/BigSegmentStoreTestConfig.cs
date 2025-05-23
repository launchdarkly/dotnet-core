using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.BigSegmentStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.BigSegmentStore
{
    /// <summary>
    /// A function that takes a prefix string and returns a configured factory for
    /// your implementation of <c>IBigSegmentStore</c>.
    /// </summary>
    /// <remarks>
    /// If the prefix string is null or "", it should use the default prefix defined by the
    /// data store implementation. The factory must include any necessary configuration that
    /// may be appropriate for the test environment (for instance, pointing it to a database
    /// instance that has been set up for the tests).
    /// </remarks>
    /// <param name="prefix">the database prefix</param>
    /// <returns>a configured factory</returns>
    public delegate IComponentConfigurer<IBigSegmentStore> StoreFactoryFunc(string prefix);

    /// <summary>
    /// An asynchronous function that removes all data from the underlying
    /// data store for the specified prefix string.
    /// </summary>
    /// <param name="prefix">the database prefix</param>
    /// <returns>an asynchronous task</returns>
    public delegate Task ClearDataAction(string prefix);

    /// <summary>
    /// An asynchronous function that updates the store metadata to the specified values.
    /// This must be provided separately by the test code because the store interface used by
    /// the SDK has no update methods.
    /// </summary>
    /// <param name="prefix">the database prefix</param>
    /// <param name="metadata">the data to write to the store</param>
    /// <returns>an asynchronous task</returns>
    public delegate Task SetMetadataAction(string prefix, StoreMetadata metadata);

    /// <summary>
    /// An asynchronous function that updates the membership state for a user in the store.
    /// This must be provided separately by the test code because the store interface used by
    /// the SDK has no update methods.
    /// </summary>
    /// <param name="prefix">the database prefix</param>
    /// <param name="userHashKey">the hashed user key</param>
    /// <param name="includedSegmentRefs">segment references to be included</param>
    /// <param name="excludedSegmentRefs">segment references to be excluded</param>
    /// <returns>an asynchronous task</returns>
    public delegate Task SetSegmentsAction(string prefix, string userHashKey,
        IEnumerable<string> includedSegmentRefs, IEnumerable<string> excludedSegmentRefs);

    /// <summary>
    /// Configuration for <see cref="BigSegmentStoreBaseTests"/>.
    /// </summary>
    public sealed class BigSegmentStoreTestConfig
    {
        public StoreFactoryFunc StoreFactoryFunc { get; set; }

        public ClearDataAction ClearDataAction { get; set; }

        public SetMetadataAction SetMetadataAction { get; set; }

        public SetSegmentsAction SetSegmentsAction { get; set; }
    }
}
