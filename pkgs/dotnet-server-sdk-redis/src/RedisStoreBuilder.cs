using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Subsystems;
using StackExchange.Redis;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// A <a href="http://en.wikipedia.org/wiki/Builder_pattern">builder</a> for configuring the
    /// Redis-based persistent data store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This can be used either for the main data store that holds feature flag data, or for the big
    /// segment store, or both. If you are using both, they do not have to have the same parameters. For
    /// instance, in this example the main data store uses a Redis host called "host1" and the big
    /// segment store uses a Redis host called "host2":
    /// </para>
    /// <code>
    ///     var config = Configuration.Builder("sdk-key")
    ///         .DataStore(
    ///             Components.PersistentDataStore(
    ///                 Redis.DataStore().Uri("redis://host1:6379")
    ///             )
    ///         )
    ///         .BigSegments(
    ///             Components.BigSegments(
    ///                 Redis.DataStore().Uri("redis://host2:6379")
    ///             )
    ///         )
    ///         .Build();
    /// </code>
    /// <para>
    /// Note that the builder is passed to one of two methods,
    /// <see cref="Components.PersistentDataStore(IComponentConfigurer{IPersistentDataStore})"/> or
    /// <see cref="Components.BigSegments(IComponentConfigurer{IBigSegmentStore})"/>, depending on the context in
    /// which it is being used. This is because each of those contexts has its own additional
    /// configuration options that are unrelated to the Redis options.
    /// </para>
    /// <para>
    /// Builder calls can be chained, for example:
    /// </para>
    /// <code>
    ///     var config = Configuration.Builder("sdk-key")
    ///         .DataStore(
    ///             Components.PersistentDataStore(
    ///                 Redis.DataStore()
    ///                     .Uri("redis://my-redis-host")
    ///                     .Database(1)
    ///                 )
    ///                 .CacheSeconds(15)
    ///             )
    ///         .Build();
    /// </code>
    /// </remarks>
    public abstract class RedisStoreBuilder<T> : IComponentConfigurer<T>, IDiagnosticDescription
    {
        internal ConfigurationOptions _redisConfig = new ConfigurationOptions();
        internal IConnectionMultiplexer _externalConnection;
        internal string _prefix = Redis.DefaultPrefix;

        internal RedisStoreBuilder()
        {
            _redisConfig.EndPoints.Add(Redis.DefaultRedisEndPoint);
            _redisConfig.ConnectTimeout = (int)Redis.DefaultConnectTimeout.TotalMilliseconds;
        }

        /// <summary>
        /// Specifies all Redis configuration options at once.
        /// </summary>
        /// <param name="config">a <see cref="ConfigurationOptions"/> instance</param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> RedisConfiguration(ConfigurationOptions config)
        {
            _redisConfig = config.Clone();
            return this;
        }

        /// <summary>
        /// Specifies a single Redis server by hostname and port.
        /// </summary>
        /// <param name="host">hostname of the Redis server</param>
        /// <param name="port">port of the Redis server</param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> HostAndPort(string host, int port) =>
            EndPoint(new DnsEndPoint(host, port));

        /// <summary>
        /// Specifies a single Redis server as an EndPoint.
        /// </summary>
        /// <param name="endPoint">location of the Redis server</param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> EndPoint(EndPoint endPoint) =>
            EndPoints(new List<EndPoint> { endPoint });

        /// <summary>
        /// Shortcut for calling <see cref="Uri(System.Uri)"/> with a string.
        /// </summary>
        /// <param name="uri">the Redis server URI as a string</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Uri(System.Uri)"/>
        public RedisStoreBuilder<T> Uri(string uri) => Uri(new Uri(uri));

        /// <summary>
        /// Specifies a Redis server - and, optionally, other properties including
        /// credentials and database number - using a URI.
        /// </summary>
        /// <param name="uri">the Redis server URI</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Uri(string)"/>
        public RedisStoreBuilder<T> Uri(Uri uri)
        {
            if (uri.Scheme.ToLower() != "redis")
            {
                throw new ArgumentException("URI scheme must be 'redis'");
            }
            HostAndPort(uri.Host, uri.Port);
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':');
                if (parts.Length == 2)
                {
                    // Redis doesn't use the username
                    _redisConfig.Password = parts[1];
                }
                else
                {
                    throw new ArgumentException("Credentials must be in the format ':password'");
                }
            }
            if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
            {
                var path = uri.AbsolutePath;
                if (path.StartsWith("/"))
                {
                    path = path.Substring(1);
                }
                var dbIndex = Int32.Parse(path);
                _redisConfig.DefaultDatabase = dbIndex;
            }
            return this;
        }

        /// <summary>
        /// Specifies multiple Redis servers as a list of EndPoints.
        /// </summary>
        /// <param name="endPoints">locations of the Redis servers</param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> EndPoints(IList<EndPoint> endPoints)
        {
            _redisConfig.EndPoints.Clear();
            foreach (var ep in endPoints)
            {
                _redisConfig.EndPoints.Add(ep);
            }
            return this;
        }

        /// <summary>
        /// Specifies which database to use within the Redis server. The default is 0.
        /// </summary>
        /// <param name="database">index of the database to use</param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> DatabaseIndex(int database)
        {
            _redisConfig.DefaultDatabase = database;
            return this;
        }

        /// <summary>
        /// Specifies the maximum time to wait for a connection to the Redis server.
        /// </summary>
        /// <param name="timeout">the timeout interval</param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> ConnectTimeout(TimeSpan timeout)
        {
            _redisConfig.ConnectTimeout = (int)timeout.TotalMilliseconds;
            return this;
        }

        /// <summary>
        /// Specifies the maximum time to wait for each synchronous Redis operation to complete.
        /// If you are seeing timeout errors - which could result from either an overburdened
        /// Redis server, or an unusually large operation such as storing a very large feature
        /// flag - you may want to increase this setting.
        /// </summary>
        /// <param name="timeout">the timeout interval</param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> OperationTimeout(TimeSpan timeout)
        {
            _redisConfig.SyncTimeout = (int)timeout.TotalMilliseconds;
            return this;
        }

        /// <summary>
        /// Specifies the namespace prefix for all keys stored in Redis.
        /// </summary>
        /// <param name="prefix">the namespace prefix, or null to use <see cref="Redis.DefaultPrefix"/></param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> Prefix(string prefix)
        {
            _prefix = string.IsNullOrEmpty(prefix) ? Redis.DefaultPrefix : prefix;
            return this;
        }

        /// <summary>
        /// Allows you to modify any of the configuration options supported by StackExchange.Redis
        /// directly. The current configuration will be passed to your Action, which can modify it
        /// in any way.
        /// </summary>
        /// <example>
        /// <code>
        ///     Redis.DataStore()
        ///         .RedisConfigChanges((config) => {
        ///             config.Ssl = true;
        ///         })
        /// </code>
        /// </example>
        /// <param name="modifyConfig"></param>
        /// <returns></returns>
        public RedisStoreBuilder<T> RedisConfigChanges(Action<ConfigurationOptions> modifyConfig)
        {
            modifyConfig.Invoke(_redisConfig);
            return this;
        }

        /// <summary>
        /// Specifies a pre-configured Redis connection multiplexer to use instead of creating one
        /// from configuration options. This allows for advanced scenarios such as Azure AAD authentication.
        /// </summary>
        /// <param name="connection">the pre-configured connection multiplexer</param>
        /// <returns>the builder</returns>
        public RedisStoreBuilder<T> Connection(IConnectionMultiplexer connection)
        {
            _externalConnection = connection ?? throw new ArgumentNullException(nameof(connection));
            return this;
        }

        /// <summary>
        /// Gets the connection to use - either the externally provided one or creates a new one from configuration.
        /// </summary>
        /// <param name="log">Logger for connection creation</param>
        /// <returns>The Redis connection multiplexer to use</returns>
        protected IConnectionMultiplexer GetOrCreateConnection(Logger log)
        {
            if (_externalConnection != null)
            {
                log.Info("Using pre-configured Redis connection with prefix \"{0}\"", _prefix);
                return _externalConnection;
            }

            log.Info("Creating Redis connection to {0} with prefix \"{1}\"",
                string.Join(", ", _redisConfig.EndPoints.Select(DescribeEndPoint)), _prefix);
            
            var redisConfigCopy = _redisConfig.Clone();
            return ConnectionMultiplexer.Connect(redisConfigCopy);
        }

        private string DescribeEndPoint(EndPoint e)
        {
            // The default ToString() method of DnsEndPoint adds a prefix of "Unspecified", which looks
            // confusing in our log messages.
            return (e is DnsEndPoint de) ?
                string.Format("{0}:{1}", de.Host, de.Port) :
                e.ToString();
        }

        /// <inheritdoc/>
        public abstract T Build(LdClientContext context);

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.Of("Redis");
    }

    internal sealed class BuilderForDataStore : RedisStoreBuilder<IPersistentDataStore>
    {
        public override IPersistentDataStore Build(LdClientContext context)
        {
            var connection = GetOrCreateConnection(context.Logger.SubLogger("DataStore.Redis"));
            return new RedisDataStoreImpl(connection, _prefix, context.Logger.SubLogger("DataStore.Redis"));
        }
    }

    internal sealed class BuilderForBigSegments : RedisStoreBuilder<IBigSegmentStore>
    {
        public override IBigSegmentStore Build(LdClientContext context)
        {
            var connection = GetOrCreateConnection(context.Logger.SubLogger("BigSegments.Redis"));
            return new RedisBigSegmentStoreImpl(connection, _prefix, context.Logger.SubLogger("BigSegments.Redis"));
        }
    }
}
