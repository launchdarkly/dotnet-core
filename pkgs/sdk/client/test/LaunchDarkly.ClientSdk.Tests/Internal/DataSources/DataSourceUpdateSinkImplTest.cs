using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal.DataStores;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    public class DataSourceUpdateSinkImplTest : BaseTest
    {
        private readonly FlagDataManager _store;
        private readonly FlagTrackerImpl _flagTracker;
        private readonly DataSourceUpdateSinkImpl _updateSink;
        
        private readonly Context _basicUser = Context.NewMulti(Context.New(ContextKind.Of("user"), "user-key1"), Context.New(ContextKind.Of("custom-kind"), "custom-key1"));
        private readonly Context _otherUser = Context.NewMulti(Context.New(ContextKind.Of("user"), "user-key2"), Context.New(ContextKind.Of("custom-kind"), "custom-key2"));

        public DataSourceUpdateSinkImplTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _store = new FlagDataManager(BasicMobileKey, null, testLogger);
            _updateSink = new DataSourceUpdateSinkImpl(_store, false, BasicTaskExecutor, testLogger);
            _flagTracker = new FlagTrackerImpl(_updateSink);
        }

        [Fact]
        public void InitPassesDataToStore()
        {
            var initData = new DataSetBuilder().Add("key1", new FeatureFlagBuilder().Build()).Build();
            _updateSink.Init(_basicUser, initData);

            Assert.Equal(initData.Items, _store.GetAll().Value.Items);
        }

        [Fact]
        public void UpsertPassesDataToStore()
        {
            var flag1a = new FeatureFlagBuilder().Version(100).Value(LdValue.Of(false)).Build();
            var initData = new DataSetBuilder().Add("key1", flag1a).Build();
            _updateSink.Init(_basicUser, initData);

            var flag1b = new FeatureFlagBuilder().Version(101).Value(LdValue.Of(true)).Build();

            _updateSink.Upsert(_basicUser, "key1", flag1b.ToItemDescriptor());

            Assert.Equal(flag1b.ToItemDescriptor(), _store.Get("key1"));
        }

        [Fact]
        public void NoEventsAreSentForFirstInit()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initData = new DataSetBuilder().Add("key1", new FeatureFlagBuilder().Build()).Build();
            _updateSink.Init(_basicUser, initData);

            events.ExpectNoValue();
        }

        [Fact]
        public void NoEventsAreSentForUpsertIfNeverInited()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            _updateSink.Upsert(_basicUser, "key1", new FeatureFlagBuilder().Build().ToItemDescriptor());

            events.ExpectNoValue();
        }

        [Fact]
        public void EventIsSentForChangedFlagOnInit()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initData1 = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(0).Build())
                .Add("key2", new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData1);

            var initData2 = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Value(LdValue.Of(false)).Variation(1).Build())
                .Add("key2", new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData2);

            var e = events.ExpectValue();
            Assert.Equal("key1", e.Key);
            Assert.Equal(LdValue.Of(true), e.OldValue);
            Assert.Equal(LdValue.Of(false), e.NewValue);
            Assert.False(e.Deleted);
        }

        [Fact]
        public void EventIsSentForAddedFlagOnInit()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initData1 = new DataSetBuilder()
                .Add("key2", new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData1);

            var initData2 = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Value(LdValue.Of(false)).Variation(1).Build())
                .Add("key2", new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData2);

            var e = events.ExpectValue();
            Assert.Equal("key1", e.Key);
            Assert.Equal(LdValue.Null, e.OldValue);
            Assert.Equal(LdValue.Of(false), e.NewValue);
            Assert.False(e.Deleted);
        }

        [Fact]
        public void EventIsSentForDeletedFlagOnInit()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initData1 = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(0).Build())
                .Add("key2", new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData1);

            var initData2 = new DataSetBuilder()
                .Add("key2", new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData2);

            var e = events.ExpectValue();
            Assert.Equal("key1", e.Key);
            Assert.Equal(LdValue.Of(true), e.OldValue);
            Assert.Equal(LdValue.Null, e.NewValue);
            Assert.True(e.Deleted);
        }

        [Fact]
        public void EventIsSentForChangedFlagOnUpsert()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initData = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Version(100).Value(LdValue.Of(true)).Variation(0).Build())
                .Add("key2", new FeatureFlagBuilder().Version(200).Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData);

            _updateSink.Upsert(_basicUser, "key1",
                new FeatureFlagBuilder().Version(101).Value(LdValue.Of(false)).Variation(1).Build().ToItemDescriptor());

            var e = events.ExpectValue();
            Assert.Equal("key1", e.Key);
            Assert.Equal(LdValue.Of(true), e.OldValue);
            Assert.Equal(LdValue.Of(false), e.NewValue);
            Assert.False(e.Deleted);
        }

        [Fact]
        public void EventIsSentForAddedFlagOnUpsert()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initData = new DataSetBuilder()
                .Add("key2", new FeatureFlagBuilder().Version(200).Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData);

            _updateSink.Upsert(_basicUser, "key1",
                new FeatureFlagBuilder().Version(100).Value(LdValue.Of(false)).Variation(1).Build().ToItemDescriptor());

            var e = events.ExpectValue();
            Assert.Equal("key1", e.Key);
            Assert.Equal(LdValue.Null, e.OldValue);
            Assert.Equal(LdValue.Of(false), e.NewValue);
            Assert.False(e.Deleted);
        }

        [Fact]
        public void EventIsSentForDeletedFlagOnUpsert()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initData = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Version(100).Value(LdValue.Of(true)).Variation(0).Build())
                .Add("key2", new FeatureFlagBuilder().Version(200).Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData);

            _updateSink.Upsert(_basicUser, "key1", new ItemDescriptor(101, null));

            var e = events.ExpectValue();
            Assert.Equal("key1", e.Key);
            Assert.Equal(LdValue.Of(true), e.OldValue);
            Assert.Equal(LdValue.Null, e.NewValue);
            Assert.True(e.Deleted);
        }

        [Fact]
        public void EventIsNotSentIfUpsertFailsDueToLowerVersion()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initData = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Version(100).Value(LdValue.Of(true)).Variation(0).Build())
                .Add("key2", new FeatureFlagBuilder().Version(200).Value(LdValue.Of(true)).Variation(0).Build())
                .Build();
            _updateSink.Init(_basicUser, initData);

            _updateSink.Upsert(_basicUser, "key1",
                new FeatureFlagBuilder().Version(99).Value(LdValue.Of(false)).Variation(1).Build().ToItemDescriptor());

            events.ExpectNoValue();
        }

        [Fact]
        public void ValueChangesAreTrackedSeparatelyForEachUser()
        {
            var events = new EventSink<FlagValueChangeEvent>();
            _flagTracker.FlagValueChanged += events.Add;

            var initDataForBasicUser = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Version(100).Value(LdValue.Of("a")).Variation(1).Build())
                .Add("key2", new FeatureFlagBuilder().Version(200).Value(LdValue.Of("b")).Variation(2).Build())
                .Build();
            _updateSink.Init(_basicUser, initDataForBasicUser);

            var initDataForOtherUser = new DataSetBuilder()
                .Add("key1", new FeatureFlagBuilder().Version(100).Value(LdValue.Of("c")).Variation(3).Build())
                .Add("key2", new FeatureFlagBuilder().Version(200).Value(LdValue.Of("d")).Variation(4).Build())
                .Build();
            _updateSink.Init(_otherUser, initDataForOtherUser);

            events.ExpectNoValue();

            _updateSink.Upsert(_basicUser, "key1",
                new FeatureFlagBuilder().Version(101).Value(LdValue.Of("c")).Variation(3).Build().ToItemDescriptor());

            var e = events.ExpectValue();

            Assert.Equal("key1", e.Key);
            Assert.Equal(LdValue.Of("a"), e.OldValue);
            Assert.Equal(LdValue.Of("c"), e.NewValue);
        }
    }
}
