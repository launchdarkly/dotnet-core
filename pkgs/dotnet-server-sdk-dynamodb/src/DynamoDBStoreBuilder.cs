using Amazon.DynamoDBv2;
using Amazon.Runtime;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// A builder for configuring the DynamoDB-based persistent data store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This can be used either for the main data store that holds feature flag data, or for the big
    /// segment store, or both. If you are using both, they do not have to have the same parameters. For
    /// instance, in this example the main data store uses a table called "table1" and the big segment
    /// store uses a table called "table2":
    /// </para>
    /// <code>
    ///     var config = Configuration.Builder("sdk-key")
    ///         .DataStore(
    ///             Components.PersistentDataStore(
    ///                 DynamoDB.DataStore("table1")
    ///             )
    ///         )
    ///         .BigSegments(
    ///             Components.BigSegments(
    ///                 DynamoDB.DataStore("table2")
    ///             )
    ///         )
    ///         .Build();
    /// </code>
    /// <para>
    /// Note that the builder is passed to one of two methods,
    /// <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStoreAsync})"/> or
    /// <see cref="Components.BigSegments(IComponentConfigurer{IBigSegmentStore})"/>, depending on the context in
    /// which it is being used. This is because each of those contexts has its own additional
    /// configuration options that are unrelated to the DynamoDB options. For instance, the
    /// <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStore})"/> builder
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
    /// <para>
    /// Builder calls can be chained, for example:
    /// </para>
    /// <code>
    ///     var config = Configuration.Builder("sdk-key")
    ///         .DataStore(
    ///             Components.PersistentDataStore(
    ///                 DynamoDB.DataStore("my-table-name")
    ///                     .Credentials(myAWSCredentials)
    ///                     .Prefix("app1")
    ///                 )
    ///                 .CacheSeconds(15)
    ///             )
    ///         .Build();
    /// </code>
    /// </remarks>
    public abstract class DynamoDBStoreBuilder<T> : IComponentConfigurer<T>, IDiagnosticDescription
    {
        internal AmazonDynamoDBClient _existingClient = null;
        internal AWSCredentials _credentials = null;
        internal AmazonDynamoDBConfig _config = null;

        internal readonly string _tableName;
        internal string _prefix = "";
        
        internal DynamoDBStoreBuilder(string tableName)
        {
            _tableName = tableName;
        }

        /// <summary>
        /// Specifies an existing, already-configured DynamoDB client instance that the data store
        /// should use rather than creating one of its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If you specify an existing client, then the other builder methods for configuring DynamoDB
        /// are ignored.
        /// </para>
        /// <para>
        /// Note that the LaunchDarkly code will <i>not</i> take ownership of the lifecycle of this
        /// object: in other words, it will not call <c>Dispose()</c> on the <c>AmazonDynamoDBClient</c> when
        /// you dispose of the SDK client, as it would if it had created the <c>AmazonDynamoDBClient</c> itself.
        /// It is your responsibility to call <c>Dispose()</c> on the <c>AmazonDynamoDBClient</c> when you are
        /// done with it.
        /// </para>
        /// </remarks>
        /// <param name="client">an existing DynamoDB client instance</param>
        /// <returns>the builder</returns>
        public DynamoDBStoreBuilder<T> ExistingClient(AmazonDynamoDBClient client)
        {
            _existingClient = client;
            return this;
        }

        /// <summary>
        /// Sets the AWS client credentials.
        /// </summary>
        /// <remarks>
        /// If you do not set them programmatically, the AWS SDK will attempt to find them in
        /// environment variables and/or local configuration files.
        /// </remarks>
        /// <param name="credentials">the AWS credentials</param>
        /// <returns>the builder</returns>
        public DynamoDBStoreBuilder<T> Credentials(AWSCredentials credentials)
        {
            _credentials = credentials;
            return this;
        }

        /// <summary>
        /// Specifies an entire DynamoDB configuration.
        /// </summary>
        /// <remarks>
        /// If this is not provided explicitly, the AWS SDK will attempt to determine your
        /// current region based on environment variables and/or local configuration files.
        /// </remarks>
        /// <param name="config">a DynamoDB configuration object</param>
        /// <returns>the builder</returns>
        public DynamoDBStoreBuilder<T> Configuration(AmazonDynamoDBConfig config)
        {
            _config = config;
            return this;
        }

        /// <summary>
        /// Sets an optional namespace prefix for all keys stored in DynamoDB.
        /// </summary>
        /// <remarks>
        /// You may use this if you are sharing the same database table between multiple clients that
        /// are for different LaunchDarkly environments, to avoid key collisions. However, in DynamoDB
        /// it is common to use separate tables rather than share a single table for unrelated
        /// applications, so by default there is no prefix.
        /// </remarks>
        /// <param name="prefix">the namespace prefix; null for no prefix</param>
        /// <returns>the builder</returns>
        public DynamoDBStoreBuilder<T> Prefix(string prefix)
        {
            _prefix = prefix;
            return this;
        }

        /// <inheritdoc/>
        public abstract T Build(LdClientContext context);

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.Of("DynamoDB");

        internal AmazonDynamoDBClient MakeClient()
        {
            if (_existingClient != null)
            {
                return _existingClient;
            }
            // Unfortunately, the AWS SDK does not believe in builders
            if (_credentials == null)
            {
                if (_config == null)
                {
                    return new AmazonDynamoDBClient();
                }
                return new AmazonDynamoDBClient(_config);
            }
            if (_config == null)
            {
                return new AmazonDynamoDBClient(_credentials);
            }
            return new AmazonDynamoDBClient(_credentials, _config);
        }
    }

    internal sealed class BuilderForDataStore : DynamoDBStoreBuilder<IPersistentDataStoreAsync>
    {
        internal BuilderForDataStore(string tableName) : base(tableName) { }

        public override IPersistentDataStoreAsync Build(LdClientContext context) =>
            new DynamoDBDataStoreImpl(
                MakeClient(),
                _existingClient != null,
                _tableName,
                _prefix,
                context.Logger.SubLogger("DataStore.DynamoDB")
                );
    }

    internal sealed class BuilderForBigSegments : DynamoDBStoreBuilder<IBigSegmentStore>
    {
        internal BuilderForBigSegments(string tableName) : base(tableName) { }

        public override IBigSegmentStore Build(LdClientContext context) =>
            new DynamoDBBigSegmentStoreImpl(
                MakeClient(),
                _existingClient != null,
                _tableName,
                _prefix,
                context.Logger.SubLogger("DataStore.DynamoDB")
                );
    }
}
