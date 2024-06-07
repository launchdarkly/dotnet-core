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
            _persistentStore.SetValue(ExpectedGlobalNamespace, ExpectedGeneratedContextKey + ":org", "key2");
            Assert.Equal("key1", _wrapper.GetGeneratedContextKey(ContextKind.Default));
            Assert.Equal("key2", _wrapper.GetGeneratedContextKey(ContextKind.Of("org")));
        }

        [Fact]
        public void SetGeneratedContextKey()
        {
            _wrapper.SetGeneratedContextKey(ContextKind.Default, "key1");
            _wrapper.SetGeneratedContextKey(ContextKind.Of("org"), "key2");
            Assert.Equal("key1", _persistentStore.GetValue(ExpectedGlobalNamespace, ExpectedGeneratedContextKey));
            Assert.Equal("key2", _persistentStore.GetValue(ExpectedGlobalNamespace, ExpectedGeneratedContextKey + ":org"));
        }
    }
}
