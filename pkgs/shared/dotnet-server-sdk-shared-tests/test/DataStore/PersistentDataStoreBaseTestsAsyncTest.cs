using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    // This runs PersistentDataStoreBaseTests against a mock store implementation that is known to
    // behave as expected, to verify that the test suite logic has the correct expectations.

    [Collection("Sequential")] // don't want this and the other test class to run simultaneously
    public class PersistentDataStoreBaseTestsAsyncTest : PersistentDataStoreBaseTests
    {
        protected override PersistentDataStoreTestConfig Configuration =>
            new PersistentDataStoreTestConfig
            {
                StoreAsyncFactoryFunc = CreateStoreFactory,
                ClearDataAction = ClearAllData,
            };

        public PersistentDataStoreBaseTestsAsyncTest(ITestOutputHelper testOutput) : base(testOutput) { }

        private IComponentConfigurer<IPersistentDataStoreAsync> CreateStoreFactory(string prefix) =>
            new MockAsyncStoreFactory { Database = MockDatabase.Instance, Prefix = prefix };

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task ClearAllData(string prefix) =>
            MockDatabase.Instance.Clear(prefix);
#pragma warning restore CS1998

        private class MockAsyncStoreFactory : IComponentConfigurer<IPersistentDataStoreAsync>
        {
            internal MockDatabase Database { get; set; }
            internal string Prefix { get; set; }

            public IPersistentDataStoreAsync Build(LdClientContext context) =>
                new MockAsyncStore(Database, Prefix);
        }
    }
}
