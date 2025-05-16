using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.BigSegmentStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.BigSegmentStore
{
    public class BigSegmentStoreBaseTestsTest : BigSegmentStoreBaseTests
    {
        // This runs BigSegmentStoreBaseTests against a mock store implementation that is known to
        // behave as expected, to verify that the test suite logic has the correct expectations.

        protected override BigSegmentStoreTestConfig Configuration =>
            new BigSegmentStoreTestConfig
            {
                StoreFactoryFunc = CreateStoreFactory,
                ClearDataAction = ClearData,
                SetMetadataAction = SetMetadata,
                SetSegmentsAction = SetSegments
            };

        private class DataSet {
            public StoreMetadata? Metadata = null;
            public Dictionary<string, IMembership> Memberships = new Dictionary<string, IMembership>();
        }

        private readonly Dictionary<string, DataSet> _allData = new Dictionary<string, DataSet>();

        public BigSegmentStoreBaseTestsTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        private IComponentConfigurer<IBigSegmentStore> CreateStoreFactory(string prefix) =>
            new MockStoreFactory(GetOrCreateDataSet(prefix));

        private Task ClearData(string prefix)
        {
            var data = GetOrCreateDataSet(prefix);
            data.Metadata = null;
            data.Memberships.Clear();
            return Task.CompletedTask;
        }

        private Task SetMetadata(string prefix, StoreMetadata metadata)
        {
            GetOrCreateDataSet(prefix).Metadata = metadata;
            return Task.CompletedTask;
        }

        private Task SetSegments(string prefix, string userHashKey,
            IEnumerable<string> includedSegmentRefs, IEnumerable<string> excludedSegmentRefs)
        {
            GetOrCreateDataSet(prefix).Memberships[userHashKey] =
                NewMembershipFromSegmentRefs(includedSegmentRefs, excludedSegmentRefs);
            return Task.CompletedTask;
        }

        private DataSet GetOrCreateDataSet(string prefix)
        {
            if (!_allData.ContainsKey(prefix))
            {
                _allData[prefix] = new DataSet();
            }
            return _allData[prefix];
        }

        private class MockStoreFactory : IComponentConfigurer<IBigSegmentStore>
        {
            private readonly DataSet _data;

            public MockStoreFactory(DataSet data)
            {
                _data = data;
            }

            public IBigSegmentStore Build(LdClientContext context) =>
                new MockStore(_data);
        }

        private class MockStore : IBigSegmentStore
        {
            private readonly DataSet _data;

            public MockStore(DataSet data)
            {
                _data = data;
            }

            public void Dispose() { }

            public Task<IMembership> GetMembershipAsync(string userHash)
            {
                if (_data.Memberships.TryGetValue(userHash, out var result))
                {
                    return Task.FromResult(result);
                }
                return Task.FromResult((IMembership)null);
            }

            public Task<StoreMetadata?> GetMetadataAsync()
            {
                return Task.FromResult(_data.Metadata);
            }
        }
    }
}
