using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    /// <summary>
    /// Configuration for <see cref="PersistentDataStoreBaseTests{StoreT}"/>.
    /// </summary>
    /// either <c>IPersistentDataStore</c> or <c>IPersistentDataStoreAsync</c></typeparam>
    public sealed class PersistentDataStoreTestConfig
    {
        /// <summary>
        /// Set this to a function that takes a prefix string and returns a configured factory for
        /// your implementation of <c>IPersistentDataStore</c>. If you implemented
        /// <c>IPersistentDataStoreAsync</c> instead, then use <c>StoreAsyncFactoryFunc</c>.
        /// </summary>
        /// <remarks>
        /// If the prefix string is null or "", it should use the default prefix defined by the
        /// data store implementation. The factory must include any necessary configuration that
        /// may be appropriate for the test environment (for instance, pointing it to a database
        /// instance that has been set up for the tests).
        /// </remarks>
        public Func<string, IComponentConfigurer<IPersistentDataStore>> StoreFactoryFunc { get; set; }

        /// <summary>
        /// Set this to a function that takes a prefix string and returns a configured factory for
        /// your implementation of <c>IPersistentDataStoreAsync</c>. If you implemented
        /// <c>IPersistentDataStore</c> instead, then use <c>StoreFactoryFunc</c>.
        /// </summary>
        /// <remarks>
        /// If the prefix string is null or "", it should use the default prefix defined by the
        /// data store implementation. The factory must include any necessary configuration that
        /// may be appropriate for the test environment (for instance, pointing it to a database
        /// instance that has been set up for the tests).
        /// </remarks>
        public Func<string, IComponentConfigurer<IPersistentDataStoreAsync>> StoreAsyncFactoryFunc { get; set; }
        
        /// <summary>
        /// Set this to an asynchronous function that removes all data from the underlying
        /// data store for the specified prefix string.
        /// </summary>
        public Func<string, Task> ClearDataAction { get; set; }

        /// <summary>
        /// Set this to enable tests of concurrent modification behavior, for store implementations
        /// that support testing this; otherwise leave it null.
        /// </summary>
        /// <remarks>
        /// The function should take two parameters: an instance of your store type (typed here as
        /// <c>object</c> because the actual implementation type is unknown to the tests), and a hook
        /// which is a synchronous <c>Action</c>. Your function should modify the store instance
        /// so that it will call the hook synchronously during each <c>Upsert</c> operation --
        /// after the old value has been read, but before the new one has been written (if those
        /// operations are not done atomically).
        /// </remarks>
        public Action<object, Action> SetConcurrentModificationHookAction { get; set; }
    }
}
