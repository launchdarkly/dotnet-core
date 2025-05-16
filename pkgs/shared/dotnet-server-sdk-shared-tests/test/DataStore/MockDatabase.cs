using System.Collections.Generic;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    public class MockDatabase
    {
        public static readonly MockDatabase Instance = new MockDatabase();

        public readonly IDictionary<string, Dictionary<DataKind, Dictionary<string, SerializedItemDescriptor>>> _data =
            new Dictionary<string, Dictionary<DataKind, Dictionary<string, SerializedItemDescriptor>>>();

        public readonly ISet<string> _inited = new HashSet<string>();

        private MockDatabase() { }

        public Dictionary<DataKind, Dictionary<string, SerializedItemDescriptor>> DataForPrefix(string prefix)
        {
            if (_data.TryGetValue(prefix ?? "", out var ret))
            {
                return ret;
            }
            var d = new Dictionary<DataKind, Dictionary<string, SerializedItemDescriptor>>();
            _data[prefix ?? ""] = d;
            return d;
        }

        public Dictionary<string, SerializedItemDescriptor> DataForPrefixAndKind(string prefix, DataKind kind)
        {
            var dfp = DataForPrefix(prefix);
            if (dfp.TryGetValue(kind, out var ret))
            {
                return ret;
            }
            var d = new Dictionary<string, SerializedItemDescriptor>();
            dfp[kind] = d;
            return d;
        }

        public void Clear(string prefix)
        {
            _data.Remove(prefix ?? "");
            _inited.Remove(prefix ?? "");
        }

        public bool Inited(string prefix) => _inited.Contains(prefix ?? "");

        public void SetInited(string prefix) => _inited.Add(prefix ?? "");
    }
}
