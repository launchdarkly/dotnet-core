using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Subsystems;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    public class PersistentDataStoreWrapperTest : BaseTest
    {
        // This verifies non-platform-dependent behavior, such as what keys we store particular
        // things under, using a mock persistent storage implementation.

        private static readonly string MobileKeyHash = Base64.UrlSafeSha256Hash(BasicMobileKey);
        private static readonly string ExpectedGlobalNamespace = "LaunchDarkly";
        private static readonly string ExpectedEnvironmentNamespace = "LaunchDarkly_" + MobileKeyHash;
        private const string UserKey = "user-key";
        private static readonly string UserHash = Base64.UrlSafeSha256Hash(UserKey);
        private static readonly string ExpectedUserFlagsKey = "flags_" + UserHash;
        private static readonly string ExpectedIndexKey = "index";
        private static readonly string ExpectedGeneratedContextKey = "anonUser";

        private readonly MockPersistentDataStore _persistentStore;
        private readonly PersistentDataStoreWrapper _wrapper;

        public PersistentDataStoreWrapperTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _persistentStore = new MockPersistentDataStore();
            _wrapper = new PersistentDataStoreWrapper(
                _persistentStore,
                BasicMobileKey,
                testLogger
                );
        }

        [Fact]
        public void GetContextDataForUnknownContext()
        {
            var data = _wrapper.GetContextData(UserKey);
            Assert.Null(data);
            Assert.Empty(logCapture.GetMessages());
        }

        [Fact]
        public void GetContextDataForKnownContextWithValidData()
        {
            var expectedData = new DataSetBuilder().Add("flagkey", 1, LdValue.Of(true), 0).Build();
            var serializedData = expectedData.ToJsonString();
            _persistentStore.SetValue(ExpectedEnvironmentNamespace, ExpectedUserFlagsKey, serializedData);

            var data = _wrapper.GetContextData(UserHash);
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(expectedData, data.Value);
            Assert.Empty(logCapture.GetMessages());
        }

        [Fact]
        public void SetUserData()
        {
            var data = new DataSetBuilder().Add("flagkey", 1, LdValue.Of(true), 0).Build();

            _wrapper.SetContextData(UserHash, data);

            var serializedData = _persistentStore.GetValue(ExpectedEnvironmentNamespace, ExpectedUserFlagsKey);
            AssertJsonEqual(data.ToJsonString(), serializedData);
        }

        [Fact]
        public void RemoveUserData()
        {
            var data = new DataSetBuilder().Add("flagkey", 1, LdValue.Of(true), 0).Build();

            _wrapper.SetContextData(UserHash, data);
            Assert.NotNull(_persistentStore.GetValue(ExpectedEnvironmentNamespace, ExpectedUserFlagsKey));

            _wrapper.RemoveContextData(UserHash);
            Assert.Null(_persistentStore.GetValue(ExpectedEnvironmentNamespace, ExpectedUserFlagsKey));
        }

        [Fact]
        public void GetIndex()
        {
            var expectedIndex = new ContextIndex().UpdateTimestamp("user1", UnixMillisecondTime.OfMillis(1000));
            _persistentStore.SetValue(ExpectedEnvironmentNamespace, ExpectedIndexKey, expectedIndex.Serialize());

            var index = _wrapper.GetIndex();
            AssertJsonEqual(expectedIndex.Serialize(), index.Serialize());
        }

        [Fact]
        public void SetIndex()
        {
            var index = new ContextIndex().UpdateTimestamp("user1", UnixMillisecondTime.OfMillis(1000));

            _wrapper.SetIndex(index);

            var serializedData = _persistentStore.GetValue(ExpectedEnvironmentNamespace, ExpectedIndexKey);
            AssertJsonEqual(index.Serialize(), serializedData);
        }

        [Fact]
        public void GetGeneratedContextKey()
        {
            _persistentStore.SetValue(ExpectedGlobalNamespace, ExpectedGeneratedContextKey, "key1");
            _persistentStore.SetValue(ExpectedGlobalNamespace, NewGeneratedContextKeyName(ContextKind.Of("org")), "key2");
            Assert.Equal("key1", _wrapper.GetGeneratedContextKey(ContextKind.Default));
            Assert.Equal("key2", _wrapper.GetGeneratedContextKey(ContextKind.Of("org")));
        }

        [Fact]
        public void SetGeneratedContextKey()
        {
            var org = ContextKind.Of("org");
            _wrapper.SetGeneratedContextKey(ContextKind.Default, "key1");
            _wrapper.SetGeneratedContextKey(org, "key2");

            Assert.Equal("key1", _persistentStore.GetValue(ExpectedGlobalNamespace, ExpectedGeneratedContextKey));
            Assert.Equal("key2", _persistentStore.GetValue(ExpectedGlobalNamespace, NewGeneratedContextKeyName(org)));

            // The contract-safe name contains no ':', and the old ':' name is never written.
            Assert.Matches("^[A-Za-z0-9_-]+$", NewGeneratedContextKeyName(org));
            Assert.Null(_persistentStore.GetValue(ExpectedGlobalNamespace, LegacyGeneratedContextKeyName(org)));
        }

        [Fact]
        public void GeneratedContextKeyNamesAreContractConformantForAllKinds()
        {
            foreach (var kind in new[] { ContextKind.Default, ContextKind.Of("ld_device"), ContextKind.Of("my.kind") })
            {
                Assert.Matches("^[A-Za-z0-9_-]+$", NewGeneratedContextKeyName(kind));
            }
        }

        [Fact]
        public void MigratesLegacyGeneratedContextKeyForwardPreservingValue()
        {
            var kind = ContextKind.Of("ld_device");
            _persistentStore.SetValue(ExpectedGlobalNamespace, LegacyGeneratedContextKeyName(kind), "legacy-guid");

            Assert.Equal("legacy-guid", _wrapper.GetGeneratedContextKey(kind));
            // Copied forward to the contract-safe name...
            Assert.Equal("legacy-guid", _persistentStore.GetValue(ExpectedGlobalNamespace, NewGeneratedContextKeyName(kind)));
            // ...and the legacy entry is left intact (copy, not move) for downgrade safety.
            Assert.Equal("legacy-guid", _persistentStore.GetValue(ExpectedGlobalNamespace, LegacyGeneratedContextKeyName(kind)));
        }

        [Fact]
        public void PrefersNewGeneratedContextKeyOverLegacy()
        {
            var kind = ContextKind.Of("ld_device");
            _persistentStore.SetValue(ExpectedGlobalNamespace, NewGeneratedContextKeyName(kind), "new-guid");
            _persistentStore.SetValue(ExpectedGlobalNamespace, LegacyGeneratedContextKeyName(kind), "legacy-guid");

            Assert.Equal("new-guid", _wrapper.GetGeneratedContextKey(kind));
            Assert.Equal("new-guid", _persistentStore.GetValue(ExpectedGlobalNamespace, NewGeneratedContextKeyName(kind)));
        }

        [Fact]
        public void TreatsEmptyGeneratedContextKeyValueAsMissing()
        {
            var kind = ContextKind.Of("ld_device");
            _persistentStore.SetValue(ExpectedGlobalNamespace, NewGeneratedContextKeyName(kind), "");

            Assert.Null(_wrapper.GetGeneratedContextKey(kind));
        }

        [Fact]
        public void LegacyReadErrorIsSwallowedAndNewWriteSucceeds()
        {
            // Models .NET Framework IsolatedStorage, which rejects any key containing ':'.
            var wrapper = new PersistentDataStoreWrapper(new ColonRejectingStore(), BasicMobileKey, testLogger);
            var kind = ContextKind.Of("ld_device");

            // New key absent; the legacy ':' probe throws but is swallowed.
            Assert.Null(wrapper.GetGeneratedContextKey(kind));

            // Persisting under the contract-safe (colon-free) name succeeds and is stable.
            wrapper.SetGeneratedContextKey(kind, "new-guid");
            Assert.Equal("new-guid", wrapper.GetGeneratedContextKey(kind));
        }

        [Fact]
        public void ReturnsStableLegacyValueWhenCopyForwardWriteFails()
        {
            var wrapper = new PersistentDataStoreWrapper(new ReadableWriteFailingStore("legacy-guid"), BasicMobileKey, testLogger);
            var kind = ContextKind.Of("ld_device");

            Assert.Equal("legacy-guid", wrapper.GetGeneratedContextKey(kind));
            Assert.Equal("legacy-guid", wrapper.GetGeneratedContextKey(kind));
        }

        [Fact]
        public void LegacyProbeDoesNotConsumeTheOneShotStoreErrorLog()
        {
            var wrapper = new PersistentDataStoreWrapper(new ProbeThrowsWriteFailsStore(), BasicMobileKey, testLogger);
            var kind = ContextKind.Of("ld_device");

            // The legacy ':' probe throws and is swallowed quietly: no store error is logged, and
            // the one-shot error slot must stay free.
            Assert.Null(wrapper.GetGeneratedContextKey(kind));
            Assert.False(logCapture.HasMessageWithRegex(LogLevel.Error, "Failure in persistent data store"));

            // A genuine store failure afterward must still surface, proving the probe did not burn
            // the one-shot slot (which would mask real errors).
            wrapper.SetGeneratedContextKey(kind, "new-guid");
            Assert.True(logCapture.HasMessageWithRegex(LogLevel.Error, "Failure in persistent data store"));
        }

        private static string NewGeneratedContextKeyName(ContextKind kind) =>
            kind.IsDefault ? ExpectedGeneratedContextKey : ExpectedGeneratedContextKey + "_" + Base64.UrlSafeEncodeNoPadding(kind.Value);

        private static string LegacyGeneratedContextKeyName(ContextKind kind) =>
            ExpectedGeneratedContextKey + ":" + kind.Value;

        // Rejects any key containing ':' by throwing, like .NET Framework IsolatedStorage paths.
        private sealed class ColonRejectingStore : IPersistentDataStore
        {
            private readonly MockPersistentDataStore _inner = new MockPersistentDataStore();
            private static void Guard(string key)
            {
                if (key.Contains(":"))
                {
                    throw new NotSupportedException("The given path's format is not supported.");
                }
            }
            public string GetValue(string storageNamespace, string key) { Guard(key); return _inner.GetValue(storageNamespace, key); }
            public void SetValue(string storageNamespace, string key, string value) { Guard(key); _inner.SetValue(storageNamespace, key, value); }
            public void Dispose() { }
        }

        // Returns a legacy value for ':' keys but fails every write, exercising the best-effort copy.
        private sealed class ReadableWriteFailingStore : IPersistentDataStore
        {
            private readonly string _legacyValue;
            public ReadableWriteFailingStore(string legacyValue) { _legacyValue = legacyValue; }
            public string GetValue(string storageNamespace, string key) => key.Contains(":") ? _legacyValue : null;
            public void SetValue(string storageNamespace, string key, string value) => throw new InvalidOperationException("writes disabled");
            public void Dispose() { }
        }

        // Throws on ':' reads (the legacy probe) and on every write (a genuine store failure),
        // to prove the quiet probe does not consume the one-shot store-error log slot.
        private sealed class ProbeThrowsWriteFailsStore : IPersistentDataStore
        {
            public string GetValue(string storageNamespace, string key)
            {
                if (key.Contains(":"))
                {
                    throw new NotSupportedException("The given path's format is not supported.");
                }
                return null;
            }
            public void SetValue(string storageNamespace, string key, string value) =>
                throw new InvalidOperationException("writes disabled");
            public void Dispose() { }
        }
    }
}
