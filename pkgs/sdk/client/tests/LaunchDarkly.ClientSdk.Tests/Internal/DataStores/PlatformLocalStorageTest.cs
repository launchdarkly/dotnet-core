using System.Linq;
using LaunchDarkly.Sdk.Client.Subsystems;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    public class PlatformLocalStorageTest : BaseTest
    {
        private const string TestNamespacePrefix = "LaunchDarkly.PlatformLocalStorageTest.";
        private const string TestNamespace1 = TestNamespacePrefix + "Things1";
        private const string TestNamespace2 = TestNamespacePrefix + "Things2";
        private const string TestNamespaceThatIsNeverSet = TestNamespacePrefix + "Unused";
        private const string TestKeyThatIsNeverSet = "unused-key";

        private static readonly IPersistentDataStore _storage = PlatformSpecific.LocalStorage.Instance;

        public PlatformLocalStorageTest(ITestOutputHelper testOutput) : base(testOutput) { }

        [Fact]
        public void GetValueUnknownNamespace()
        {
            _storage.SetValue(TestNamespace1, "key1", "x");
            Assert.Null(_storage.GetValue(TestNamespaceThatIsNeverSet, "key1"));
        }

        [Fact]
        public void GetValueUnknownKey()
        {
            _storage.SetValue(TestNamespace1, "key1", "x");
            Assert.Null(_storage.GetValue(TestNamespace1, TestKeyThatIsNeverSet));
        }

        [Fact]
        public void GetAndSetValues()
        {
            _storage.SetValue(TestNamespace1, "key1", "value1a");
            _storage.SetValue(TestNamespace1, "key2", "value2a");
            _storage.SetValue(TestNamespace2, "key1", "value1b");
            _storage.SetValue(TestNamespace2, "key2", "value2b");

            Assert.Equal("value1a", _storage.GetValue(TestNamespace1, "key1"));
            Assert.Equal("value2a", _storage.GetValue(TestNamespace1, "key2"));
            Assert.Equal("value1b", _storage.GetValue(TestNamespace2, "key1"));
            Assert.Equal("value2b", _storage.GetValue(TestNamespace2, "key2"));
        }

        [Fact]
        public void RemoveValues()
        {
            _storage.SetValue(TestNamespace1, "key1", "value1a");
            _storage.SetValue(TestNamespace1, "key2", "value2a");
            _storage.SetValue(TestNamespace2, "key1", "value1b");
            _storage.SetValue(TestNamespace2, "key2", "value2b");

            _storage.SetValue(TestNamespace1, "key1", null);
            _storage.SetValue(TestNamespace2, "key2", null);

            Assert.Null(_storage.GetValue(TestNamespace1, "key1"));
            Assert.Equal("value2a", _storage.GetValue(TestNamespace1, "key2"));
            Assert.Equal("value1b", _storage.GetValue(TestNamespace2, "key1"));
            Assert.Null(_storage.GetValue(TestNamespace2, "key2"));
        }

        [Fact]
        public void RemoveUnknownKey()
        {
            _storage.SetValue(TestNamespace1, "key1", "x");
            _storage.SetValue(TestNamespace1, "key2", null);

            Assert.Equal("x", _storage.GetValue(TestNamespace1, "key1"));
        }

        [Fact]
        public void KeysWithSpecialCharacters()
        {
            var keys = new string[]
            {
                "-",
                "_",
                "key-with-dashes",
                "key_with_underscores"
            };
            var keysAndValues = keys.ToDictionary(key => key, key => "value-" + key);
            foreach (var k in keysAndValues.Keys)
            {
                var ns = TestNamespacePrefix + nameof(KeysWithSpecialCharacters) + k;
                foreach (var kv in keysAndValues)
                {
                    testLogger.Info("*** setting {0} to {1}", kv.Key, kv.Value);
                    _storage.SetValue(ns, kv.Key, kv.Value);
                }
                foreach (var kv in keysAndValues)
                {
                    Assert.Equal(kv.Value, _storage.GetValue(ns, kv.Key));
                }
            }
        }
    }
}
