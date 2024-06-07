using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    public class FlagDataManagerTest : BaseTest
    {
        private readonly FlagDataManager _store;

        public FlagDataManagerTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _store = new FlagDataManager(BasicMobileKey, null, testLogger);
        }

        [Fact]
        public void GetCachedDataReturnsNullWithPersistenceDisabled()
        {
            Assert.Null(_store.GetCachedData(BasicUser));
        }

        [Fact]
        public void PersistentStoreIsNullWithPersistenceDisabled()
        {
            Assert.Null(_store.PersistentStore);
        }

        [Fact]
        public void GetUnknownFlagWhenNotInitialized()
        {
            Assert.Null(_store.Get("flagkey"));
        }

        [Fact]
        public void GetUnknownFlagKeyAfterInitialized()
        {
            var initData = new DataSetBuilder()
                .Add("flag1", 1, LdValue.Of(true), 0)
                .Build();
            _store.Init(BasicUser, initData, false);

            Assert.Null(_store.Get("flag2"));
        }

        [Fact]
        public void GetKnownFlag()
        {
            var flag1 = new FeatureFlagBuilder().Version(100).Value(LdValue.Of(true)).Build();
            var initData = new DataSetBuilder()
                .Add("flag1", flag1)
                .Build();
            _store.Init(BasicUser, initData, false);

            Assert.Equal(flag1.ToItemDescriptor(), _store.Get("flag1"));
        }

        [Fact]
        public void GetDeletedFlagForKnownUser()
        {
            var initData = new DataSetBuilder()
                .AddDeleted("flag1", 200)
                .Build();
            _store.Init(BasicUser, initData, false);

            Assert.Equal(new ItemDescriptor(200, null), _store.Get("flag1"));
        }

        [Fact]
        public void GetAllWhenNotInitialized()
        {
            var data = _store.GetAll();
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(DataSetBuilder.Empty, data.Value);
        }

        [Fact]
        public void GetAllWithEmptyFlags()
        {
            _store.Init(BasicUser, DataSetBuilder.Empty, false);

            var data = _store.GetAll();
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(DataSetBuilder.Empty, data.Value);
        }

        [Fact]
        public void GetAllReturnsFlags()
        {
            var initData = new DataSetBuilder()
                .Add("flag1", 1, LdValue.Of(true), 0)
                .Add("flag2", 2, LdValue.Of(false), 1)
                .Build();
            _store.Init(BasicUser, initData, false);

            var data = _store.GetAll();
            Assert.NotNull(data);
            AssertHelpers.DataSetsEqual(initData, data.Value);
        }

        [Fact]
        public void UpsertAddsFlag()
        {
            var flag1 = new FeatureFlagBuilder().Version(100).Value(LdValue.Of(true)).Build();
            var flag2 = new FeatureFlagBuilder().Version(200).Value(LdValue.Of(false)).Build();
            var initData = new DataSetBuilder()
                .Add("flag1", flag1)
                .Build();
            _store.Init(BasicUser, initData, false);

            var updated = _store.Upsert("flag2", flag2.ToItemDescriptor());
            Assert.True(updated);

            Assert.Equal(flag2.ToItemDescriptor(), _store.Get("flag2"));
        }

        [Fact]
        public void UpsertUpdatesFlag()
        {
            var flag1a = new FeatureFlagBuilder().Version(100).Value(LdValue.Of(true)).Build();
            var initData = new DataSetBuilder()
                .Add("flag1", flag1a)
                .Build();
            _store.Init(BasicUser, initData, false);

            var flag1b = new FeatureFlagBuilder().Version(101).Value(LdValue.Of(false)).Build();
            var updated = _store.Upsert("flag1", flag1b.ToItemDescriptor());
            Assert.True(updated);

            Assert.Equal(flag1b.ToItemDescriptor(), _store.Get("flag1"));
        }

        [Fact]
        public void UpsertDeletesFlag()
        {
            var flag1 = new FeatureFlagBuilder().Version(100).Value(LdValue.Of(true)).Build();
            var initData = new DataSetBuilder()
                .Add("flag1", flag1)
                .Build();
            _store.Init(BasicUser, initData, false);

            var flag1Deleted = new ItemDescriptor(101, null);
            var updated = _store.Upsert("flag1", flag1Deleted);
            Assert.True(updated);

            Assert.Equal(flag1Deleted, _store.Get("flag1"));
        }

        [Fact]
        public void UpsertUndeletesFlag()
        {
            var initData = new DataSetBuilder()
                .AddDeleted("flag1", 100)
                .Build();
            _store.Init(BasicUser, initData, false);

            var flag1 = new FeatureFlagBuilder().Version(101).Value(LdValue.Of(true)).Build();

            var updated = _store.Upsert("flag1", flag1.ToItemDescriptor());
            Assert.True(updated);

            Assert.Equal(flag1.ToItemDescriptor(), _store.Get("flag1"));
        }

        [Theory]
        [InlineData(100, 100)]
        [InlineData(100, 99)]
        public void UpsertDoesNotUpdateFlagWithEqualOrLowerVersion(int previousVersion, int newVersion)
        {
            var flag1a = new FeatureFlagBuilder().Version(previousVersion).Value(LdValue.Of(true)).Build();
            var initData = new DataSetBuilder()
                .Add("flag1", flag1a)
                .Build();

            _store.Init(BasicUser, initData, false);

            var flag1b = new FeatureFlagBuilder().Version(newVersion).Value(LdValue.Of(false)).Build();

            var updated = _store.Upsert("flag1", flag1b.ToItemDescriptor());
            Assert.False(updated);

            Assert.Equal(flag1a.ToItemDescriptor(), _store.Get("flag1"));
        }

        [Theory]
        [InlineData(100, 100)]
        [InlineData(100, 99)]
        public void UpsertDoesNotDeleteFlagWithEqualOrLowerVersion(int previousVersion, int newVersion)
        {
            var flag1a = new FeatureFlagBuilder().Version(previousVersion).Value(LdValue.Of(true)).Build();
            var initData = new DataSetBuilder()
                .Add("flag1", flag1a)
                .Build();

            _store.Init(BasicUser, initData, false);

            var deletedDesc = new ItemDescriptor(newVersion, null);

            var updated = _store.Upsert("flag1", deletedDesc);
            Assert.False(updated);

            Assert.Equal(flag1a.ToItemDescriptor(), _store.Get("flag1"));
        }
    }
}
