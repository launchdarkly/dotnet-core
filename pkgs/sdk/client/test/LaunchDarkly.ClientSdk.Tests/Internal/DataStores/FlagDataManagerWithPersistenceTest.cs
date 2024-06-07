using System.Collections.Immutable;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    public class FlagDataManagerWithPersistenceTest : BaseTest
    {
        private static readonly Context OtherUser = Context.New("other-user");
        private static readonly FullDataSet DataSet1 = new DataSetBuilder()
            .Add("flag1", 1, LdValue.Of(true), 0)
            .Add("flag2", 2, LdValue.Of(false), 1)
            .Build();
        private static readonly FullDataSet DataSet2 = new DataSetBuilder()
            .Add("flag3", 1, LdValue.Of(true), 0)
            .Build();

        private readonly MockPersistentDataStore _persistentStore;

        public FlagDataManagerWithPersistenceTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _persistentStore = new MockPersistentDataStore();
        }

        internal FlagDataManager MakeStore(int maxCachedUsers) =>
            new FlagDataManager(BasicMobileKey,
                new PersistenceConfiguration(_persistentStore, maxCachedUsers), testLogger);

        [Fact]
        public void PersistentStoreIsNullIfMaxCachedUsersIsZero()
        {
            Assert.Null(MakeStore(0).PersistentStore);
        }

        [Fact]
        public void PersistentStoreIsNullWithNoOpStoreImplementation()
        {
            var store = new FlagDataManager(BasicMobileKey,
                new PersistenceConfiguration(NullPersistentDataStore.Instance, 5), testLogger);
            Assert.Null(store.PersistentStore);
        }

        [Fact]
        public void PersistentStoreIsNotNullWithValidStoreImplementationAndNonZeroUsers()
        {
            Assert.NotNull(MakeStore(5).PersistentStore);
        }

        [Fact]
        public void GetCachedDataForUnknownUser()
        {
            var store = MakeStore(1);
            Assert.Null(store.GetCachedData(BasicUser));
        }

        [Fact]
        public void GetCachedDataForKnownUser()
        {
            _persistentStore.SetupUserData(BasicMobileKey, BasicUser.Key, DataSet1);
            _persistentStore.SetupUserData(BasicMobileKey, OtherUser.Key, DataSet2);

            var store = MakeStore(1);

            var data = store.GetCachedData(BasicUser);
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(DataSet1, data.Value);
        }

        [Fact]
        public void InitWritesToPersistentStoreIfToldTo()
        {
            var store = MakeStore(1);
            store.Init(BasicUser, DataSet1, true);

            var data = _persistentStore.InspectUserData(BasicMobileKey, BasicUser.Key);
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(DataSet1, data.Value);
        }

        [Fact]
        public void InitDoesNotWriteToPersistentStoreIfToldNotTo()
        {
            _persistentStore.SetupUserData(BasicMobileKey, BasicUser.Key, DataSet1);

            var store = MakeStore(1);

            var data = store.GetCachedData(BasicUser);
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(DataSet1, data.Value);

            // Hack the underlying store to remove the data so we can tell if it gets rewritten
            _persistentStore.SetupUserData(BasicMobileKey, BasicUser.Key, DataSetBuilder.Empty);

            store.Init(BasicUser, data.Value, false);

            // Because we passed false in Init, it does not rewrite the data - this behavior is to
            // avoid unnecessary writes on startup when we've just read the data from the cache.
            var underlyingData = _persistentStore.InspectUserData(BasicMobileKey, BasicUser.Key);
            Assert.NotNull(underlyingData);
            AssertHelpers.DataSetsEqual(DataSetBuilder.Empty, underlyingData.Value);
        }

        [Fact]
        public void InitUpdatesIndex()
        {
            var store = MakeStore(2);

            store.Init(BasicUser, DataSet1, true);
            store.Init(OtherUser, DataSet2, true);

            var index = _persistentStore.InspectContextIndex(BasicMobileKey);
            Assert.Equal(
                ImmutableList.Create(Base64.UrlSafeSha256Hash(BasicUser.Key), Base64.UrlSafeSha256Hash(OtherUser.Key)),
                index.Data.Select(e => e.ContextId).ToImmutableList()
                );
        }

        [Fact]
        public void InitEvictsLeastRecentUser()
        {
            var dataSet3 = new DataSetBuilder()
                .Add("flag4", 4, LdValue.Of(false), 1)
                .Build();
            var user3 = Context.New("third-user");

            var store = MakeStore(2);
            store.Init(BasicUser, DataSet1, true);
            store.Init(OtherUser, DataSet2, true);
            store.Init(user3, dataSet3, true);

            var index = _persistentStore.InspectContextIndex(BasicMobileKey);
            Assert.Equal(
                ImmutableList.Create(Base64.UrlSafeSha256Hash(OtherUser.Key), Base64.UrlSafeSha256Hash(user3.Key)),
                index.Data.Select(e => e.ContextId).ToImmutableList()
                );
        }

        [Fact]
        public void GetDoesNotReadFromPersistentStore()
        {
            var flag1a = new FeatureFlagBuilder().Version(1).Value(true).Build();
            var flag1b = new FeatureFlagBuilder().Version(2).Value(false).Build();
            var data1a = new DataSetBuilder().Add("flag1", flag1a).Build();
            var data1b = new DataSetBuilder().Add("flag1", flag1b).Build();

            var store = MakeStore(1);
            store.Init(BasicUser, data1a, true);

            // Hack the underlying store to change the data so we can verify it isn't being reread
            _persistentStore.SetupUserData(BasicMobileKey, BasicUser.Key, data1b);

            var item = store.Get("flag1");
            Assert.Equal(flag1a.ToItemDescriptor(), item);
        }

        [Fact]
        public void GetAllDoesNotReadFromPersistentStore()
        {
            var flag1 = new FeatureFlagBuilder().Version(1).Value(true).Build();
            var flag2 = new FeatureFlagBuilder().Version(2).Value(false).Build();
            var data1 = new DataSetBuilder().Add("flag1", flag1).Build();
            var data2 = new DataSetBuilder().Add("flag2", flag2).Build();

            var store = MakeStore(1);
            store.Init(BasicUser, data1, true);

            // Hack the underlying store to change the data so we can verify it isn't being reread
            _persistentStore.SetupUserData(BasicMobileKey, BasicUser.Key, data2);

            var data = store.GetAll();
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(data1, data.Value);
        }

        [Fact]
        public void UpsertUpdatesPersistentStore()
        {
            var flag1a = new FeatureFlagBuilder().Version(1).Value(true).Build();
            var flag1b = new FeatureFlagBuilder().Version(2).Value(true).Build();
            var flag2 = new FeatureFlagBuilder().Version(1).Value(false).Build();
            var data1a = new DataSetBuilder().Add("flag1", flag1a).Add("flag2", flag2).Build();
            var data1b = new DataSetBuilder().Add("flag1", flag1b).Add("flag2", flag2).Build();

            var store = MakeStore(1);
            store.Init(BasicUser, data1a, true);

            var updated = store.Upsert("flag1", flag1b.ToItemDescriptor());
            Assert.True(updated);

            var item = store.Get("flag1"); // this is reading only from memory, not the persistent store
            Assert.Equal(flag1b.ToItemDescriptor(), item);

            var data = _persistentStore.InspectUserData(BasicMobileKey, BasicUser.Key);
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(data1b, data.Value);
        }

        [Fact]
        public void UpsertDoesNotUpdatePersistentStoreIfUpdateIsUnsuccessful()
        {
            var flag1a = new FeatureFlagBuilder().Version(100).Value(true).Build();
            var flag1b = new FeatureFlagBuilder().Version(99).Value(true).Build();
            var flag2 = new FeatureFlagBuilder().Version(1).Value(false).Build();
            var data1a = new DataSetBuilder().Add("flag1", flag1a).Add("flag2", flag2).Build();

            var store = MakeStore(1);
            store.Init(BasicUser, data1a, true);

            var updated = store.Upsert("flag1", flag1b.ToItemDescriptor());
            Assert.False(updated);

            var item = store.Get("flag1"); // this is reading only from memory, not the persistent store
            Assert.Equal(flag1a.ToItemDescriptor(), item);

            var data = _persistentStore.InspectUserData(BasicMobileKey, BasicUser.Key);
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(data1a, data.Value);
        }

        [Fact]
        public void FlagsAreStoredByFullyQualifiedKeyForSingleAndMultiKindContexts()
        {
            // This tests that we are correctly disambiguating contexts based on their FullyQualifiedKey,
            // which includes both key and kind information (and, for multi-kind contexts, is based on
            // concatenating the individual kinds). If we were using only the Key property, these users
            // would collide.
            var contexts = new Context[]
            {
                Context.New("key1"),
                Context.New("key2"),
                Context.New(ContextKind.Of("kind2"), "key1"),
                Context.NewMulti(Context.New(ContextKind.Of("kind1"), "key1"), Context.New(ContextKind.Of("kind2"), "key2")),
                Context.NewMulti(Context.New(ContextKind.Of("kind1"), "key1"), Context.New(ContextKind.Of("kind2"), "key3"))
            };
            var store = MakeStore(contexts.Length);
            for (var i = 0; i < contexts.Length; i++)
            {
                var initData = new DataSetBuilder().Add("flag", 1, LdValue.Of(i), 0).Build();
                store.Init(contexts[i], initData, true);
            }
            for (var i = 0; i < contexts.Length; i++)
            {
                var data = store.GetCachedData(contexts[i]);
                Assert.NotNull(data);
                var flagValue = data.Value.Items[0].Value.Item.Value;
                Assert.Equal(LdValue.Of(i), flagValue);
            }
        }
    }
}
