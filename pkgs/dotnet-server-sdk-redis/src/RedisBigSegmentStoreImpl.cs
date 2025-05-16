using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.Logging;
using StackExchange.Redis;

using static LaunchDarkly.Sdk.Server.Subsystems.BigSegmentStoreTypes;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    internal sealed class RedisBigSegmentStoreImpl : RedisStoreImplBase, IBigSegmentStore
    {
        private readonly string _syncTimeKey;
        private readonly string _includedKeyPrefix;
        private readonly string _excludedKeyPrefix;

        internal RedisBigSegmentStoreImpl(
            ConfigurationOptions redisConfig,
            string prefix,
            Logger log
            ) : base(redisConfig, prefix, log)
        {
            _syncTimeKey = prefix + ":big_segments_synchronized_on";
            _includedKeyPrefix = prefix + ":big_segment_include:";
            _excludedKeyPrefix = prefix + ":big_segment_exclude:";
        }

        public async Task<IMembership> GetMembershipAsync(string userHash)
        {
            var db = _redis.GetDatabase();

            var includedRefs = await db.SetMembersAsync(_includedKeyPrefix + userHash);
            var excludedRefs = await db.SetMembersAsync(_excludedKeyPrefix + userHash);

            return NewMembershipFromSegmentRefs(RedisValuesToStrings(includedRefs),
                RedisValuesToStrings(excludedRefs));
        }

        public async Task<StoreMetadata?> GetMetadataAsync()
        {
            var db = _redis.GetDatabase();

            var value = await db.StringGetAsync(_syncTimeKey);
            if (value.IsNull)
            {
                return null;
            }
            if (value == "")
            {
                return new StoreMetadata { LastUpToDate = null };
            }
            var millis = long.Parse(value);
            return new StoreMetadata { LastUpToDate = UnixMillisecondTime.OfMillis(millis) };
        }

        private static IEnumerable<string> RedisValuesToStrings(RedisValue[] values) =>
            (values is null) ? null : values.Select(v => v.ToString());
    }
}
