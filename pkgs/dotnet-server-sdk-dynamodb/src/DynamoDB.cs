using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Integration between the LaunchDarkly SDK and DynamoDB.
    /// </summary>
    public static class DynamoDB
    {
        /// <summary>
        /// Name of the partition key that the data store's table must have. You must specify
        /// this when you create the table. The key type must be String.
        /// </summary>
        public const string DataStorePartitionKey = "namespace";

        /// <summary>
        /// Name of the sort key that the data store's table must have. You must specify this
        /// when you create the table. The key type must be String.
        /// </summary>
        public const string DataStoreSortKey = "key";

        /// <summary>
        /// Returns a builder object for creating a Redis-backed persistent data store.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is for the main data store that holds feature flag data. To configure a
        /// Big Segment store, use <see cref="BigSegmentStore"/> instead.
        /// </para>
        /// <para>
        /// You can use methods of the builder to specify any non-default DynamoDB options
        /// you may want, before passing the builder to
        /// <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStoreAsync})"/>.
        /// In this example, the store is configured to use a table called "table1":
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.PersistentDataStore(
        ///                 DynamoDB.DataStore("table1")
        ///             )
        ///         )
        ///         .Build();
        /// </code>
        /// <para>
        /// Note that the SDK also has its own options related to data storage that are configured
        /// at a different level, because they are independent of what database is being used. For
        /// instance, the builder returned by <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStoreAsync})"/>
        /// has options for caching:
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.PersistentDataStore(
        ///                 DynamoDB.DataStore("table1")
        ///             ).CacheSeconds(15)
        ///         )
        ///         .Build();
        /// </code>
        /// </remarks>
        /// <param name="tableName">the DynamoDB table name; this table must already exist</param>
        /// <returns>a data store configuration object</returns>
        public static DynamoDBStoreBuilder<IPersistentDataStoreAsync> DataStore(string tableName) =>
            new BuilderForDataStore(tableName);

        /// <summary>
        /// Returns a builder object for creating a DynamoDB-backed Big Segment store.
        /// </summary>
        /// <remarks>
        /// <para>
        /// You can use methods of the builder to specify any non-default DynamoDB options
        /// you may want, before passing the builder to
        /// <see cref="Components.BigSegments(IComponentConfigurer{IBigSegmentStore})"/>.
        /// In this example, the store is configured to use a table called "table2":
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.BigSegments(
        ///                 DynamoDB.BigSegmentStore("table2")
        ///             )
        ///         )
        ///         .Build();
        /// </code>
        /// <para>
        /// Note that the SDK also has its own options related to Big Segments that are configured
        /// at a different level, because they are independent of what database is being used. For
        /// instance, the builder returned by <see cref="Components.BigSegments(IComponentConfigurer{IBigSegmentStore})"/>
        /// has an option for the status polling interval:
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.BigSegments(
        ///                 DynamoDB.BigSegmentStore("table2")
        ///             ).StatusPollInterval(TimeSpan.FromSeconds(30))
        ///         )
        ///         .Build();
        /// </code>
        /// </remarks>
        /// <param name="tableName">the DynamoDB table name; this table must already exist</param>
        /// <returns>a Big Segment store configuration object</returns>
        public static DynamoDBStoreBuilder<IBigSegmentStore> BigSegmentStore(string tableName) =>
            new BuilderForBigSegments(tableName);
    }
}
