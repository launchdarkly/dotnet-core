using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    public class MockSyncStore : IPersistentDataStore
    {
        private readonly MockDatabase _db;
        private readonly string _prefix;

        public MockSyncStore(MockDatabase db, string prefix)
        {
            _db = db;
            _prefix = prefix ?? "";
        }

        public void Dispose() { }

        public SerializedItemDescriptor? Get(DataKind kind, string key) =>
            _db.DataForPrefixAndKind(_prefix, kind).TryGetValue(key, out var ret) ?
                ret : (SerializedItemDescriptor?)null;

        public KeyedItems<SerializedItemDescriptor> GetAll(DataKind kind) =>
            new KeyedItems<SerializedItemDescriptor>(_db.DataForPrefixAndKind(_prefix, kind));

        public void Init(FullDataSet<SerializedItemDescriptor> allData)
        {
            _db.DataForPrefix(_prefix).Clear();
            foreach (var coll in allData.Data)
            {
                _db.DataForPrefix(_prefix)[coll.Key] = new Dictionary<string, SerializedItemDescriptor>(coll.Value.Items);
            }
            _db.SetInited(_prefix);
        }

        public bool Initialized() => _db.Inited(_prefix);

        public bool IsStoreAvailable() => true;

        public bool Upsert(DataKind kind, string key, SerializedItemDescriptor item)
        {
            var dict = _db.DataForPrefixAndKind(_prefix, kind);
            if (dict.TryGetValue(key, out var oldItem) && oldItem.Version >= item.Version)
            {
                return false;
            }
            dict[key] = item;
            return true;
        }
    }
}
