using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    // MockAsyncStore is defined as a simple wrapper around MockSyncStore because we're not trying to
    // test any real asynchronous functionality in the data store itself; we're just testing that the
    // SDK makes the appropriate calls to the IPersistentDataStoreAsync API.

    public class MockAsyncStore : IPersistentDataStoreAsync
    {
        private readonly MockSyncStore _syncStore;

        public MockAsyncStore(MockDatabase db, string prefix)
        {
            _syncStore = new MockSyncStore(db, prefix);
        }

        public void Dispose() { }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public async Task<SerializedItemDescriptor?> GetAsync(DataKind kind, string key) =>
            _syncStore.Get(kind, key);

        public async Task<KeyedItems<SerializedItemDescriptor>> GetAllAsync(DataKind kind) =>
            _syncStore.GetAll(kind);

        public async Task InitAsync(FullDataSet<SerializedItemDescriptor> allData) =>
            _syncStore.Init(allData);

        public async Task<bool> InitializedAsync() => _syncStore.Initialized();

        public async Task<bool> IsStoreAvailableAsync() => true;

        public async Task<bool> UpsertAsync(DataKind kind, string key, SerializedItemDescriptor item) =>
            _syncStore.Upsert(kind, key, item);

#pragma warning restore CS1998
    }
}
