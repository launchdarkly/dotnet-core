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
        protected readonly IConnectionMultiplexer _redis;
        protected readonly string _prefix;
        protected readonly Logger _log;

        protected RedisStoreImplBase(
            IConnectionMultiplexer redis,
            string prefix,
            Logger log
            )
        {
            _log = log;
            _redis = redis;
            _prefix = prefix;
            _log.Info("Using Redis connection with prefix \"{0}\"", prefix);
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

    }
}
