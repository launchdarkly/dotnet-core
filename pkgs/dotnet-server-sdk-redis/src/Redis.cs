using System;
using System.Net;
using System.Net.NetworkInformation;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Integration between the LaunchDarkly SDK and Redis.
    /// </summary>
    public static class Redis
    {
        /// <summary>
        /// The default location of the Redis server: <c>localhost:6379</c>
        /// </summary>
        public static readonly EndPoint DefaultRedisEndPoint = new DnsEndPoint("localhost", 6379);

        /// <summary>
        /// The default value for <see cref="RedisStoreBuilder{T}.Prefix"/>.
        /// </summary>
        public static readonly string DefaultPrefix = "launchdarkly";

        /// <summary>
        /// The default value for <see cref="RedisStoreBuilder{T}.ConnectTimeout(TimeSpan)"/>.
        /// </summary>
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The default value for <see cref="RedisStoreBuilder{T}.OperationTimeout(TimeSpan)"/>.
        /// </summary>
        public static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Returns a builder object for creating a Redis-backed persistent data store.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is for the main data store that holds feature flag data. To configure a
        /// Big Segment store, use <see cref="BigSegmentStore"/> instead.
        /// </para>
        /// <para>
        /// You can use methods of the builder to specify any non-default Redis options
        /// you may want, before passing the builder to
        /// <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStore})"/>.
        /// In this example, the store is configured to use a Redis host called "host1":
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.PersistentDataStore(
        ///                 Redis.DataStore().Uri("redis://host1:6379")
        ///             )
        ///         )
        ///         .Build();
        /// </code>
        /// <para>
        /// Note that the SDK also has its own options related to data storage that are configured
        /// at a different level, because they are independent of what database is being used. For
        /// instance, the builder returned by <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStore})"/>
        /// has options for caching:
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.PersistentDataStore(
        ///                 Redis.DataStore().Uri("redis://my-redis-host")
        ///             ).CacheSeconds(15)
        ///         )
        ///         .Build();
        /// </code>
        /// </remarks>
        /// <returns>a data store configuration object</returns>
        public static RedisStoreBuilder<IPersistentDataStore> DataStore() =>
            new BuilderForDataStore();

        /// <summary>
        /// Returns a builder object for creating a Redis-backed Big Segment store.
        /// </summary>
        /// <remarks>
        /// <para>
        /// You can use methods of the builder to specify any non-default Redis options
        /// you may want, before passing the builder to
        /// <see cref="Components.BigSegments(IComponentConfigurer{IBigSegmentStore})"/>.
        /// In this example, the store is configured to use a Redis host called "host2":
        /// </para>
        /// <code>
        ///     var config = Configuration.Builder("sdk-key")
        ///         .DataStore(
        ///             Components.BigSegments(
        ///                 Redis.BigSegmentStore().Uri("redis://host2:6379")
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
        ///                 Redis.BigSegmentStore().Uri("redis://my-redis-host")
        ///             ).StatusPollInterval(TimeSpan.FromSeconds(30))
        ///         )
        ///         .Build();
        /// </code>
        /// </remarks>
        /// <returns>a Big Segment store configuration object</returns>
        public static RedisStoreBuilder<IBigSegmentStore> BigSegmentStore() =>
            new BuilderForBigSegments();
    }
}
