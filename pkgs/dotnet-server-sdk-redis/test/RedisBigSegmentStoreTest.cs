using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.Sdk.Server.SharedTests.BigSegmentStore;
using StackExchange.Redis;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.BigSegmentStoreTypes;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class RedisBigSegmentStoreTest : BigSegmentStoreBaseTests
    {
        private readonly ConnectionMultiplexer _redis;

        override protected BigSegmentStoreTestConfig Configuration => new BigSegmentStoreTestConfig
        {
            StoreFactoryFunc = MakeStoreFactory,
            ClearDataAction = ClearData,
            SetMetadataAction = SetMetadata,
            SetSegmentsAction = SetSegments
        };

        public RedisBigSegmentStoreTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _redis = ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true");
        }

        private IComponentConfigurer<IBigSegmentStore> MakeStoreFactory(string prefix) =>
            Redis.BigSegmentStore().Prefix(prefix);

        private async Task ClearData(string prefix) =>
            await RedisDataStoreTest.ClearDataWithPrefix(_redis, prefix);

        private async Task SetMetadata(string prefix, StoreMetadata metadata) =>
            await _redis.GetDatabase().StringSetAsync(
                prefix + ":big_segments_synchronized_on",
                metadata.LastUpToDate.HasValue ?
                    metadata.LastUpToDate.Value.Value.ToString() :
                    ""
                );

        private async Task SetSegments(string prefix, string userHash,
            IEnumerable<string> includedRefs, IEnumerable<string> excludedRefs)
        {
            var db = _redis.GetDatabase();

            var includeKey = prefix + ":big_segment_include:" + userHash;
            var excludeKey = prefix + ":big_segment_exclude:" + userHash;
            foreach (var r in includedRefs)
            {
                await db.SetAddAsync(includeKey, r);
            }
            foreach (var r in excludedRefs)
            {
                await db.SetAddAsync(excludeKey, r);
            }
        }
    }
}
