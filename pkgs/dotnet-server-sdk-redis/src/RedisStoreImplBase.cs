using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Subsystems;
using StackExchange.Redis;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    internal abstract class RedisStoreImplBase : IDisposable
    {
        protected readonly ConnectionMultiplexer _redis;
        protected readonly string _prefix;
        protected readonly Logger _log;

        protected RedisStoreImplBase(
            ConfigurationOptions redisConfig,
            string prefix,
            Logger log
            )
        {
            _log = log;
            var redisConfigCopy = redisConfig.Clone();
            _redis = ConnectionMultiplexer.Connect(redisConfigCopy);
            _prefix = prefix;
            _log.Info("Using Redis data store at {0} with prefix \"{1}\"",
                string.Join(", ", redisConfig.EndPoints.Select(DescribeEndPoint)), prefix);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _redis.Dispose();
            }
        }

        private string DescribeEndPoint(EndPoint e)
        {
            // The default ToString() method of DnsEndPoint adds a prefix of "Unspecified", which looks
            // confusing in our log messages.
            return (e is DnsEndPoint de) ?
                string.Format("{0}:{1}", de.Host, de.Port) :
                e.ToString();
        }
    }
}
