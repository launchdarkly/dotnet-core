using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    /// <summary>
    /// A configurable Xunit test class for all implementations of <c>IPersistentDataStore</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each implementation of those interfaces should define a test class that is a subclass of this
    /// class for their implementation type, and run it in the unit tests for their project.
    /// </para>
    /// <para>
    /// In order to be testable with this class, a data store implementation must have the following
    /// characteristics:
    /// </para>
    /// <list type="number">
    /// <item>It has some notion of a "prefix" string that can be used to distinguish between different
    /// SDK instances using the same underlying database.</item>
    /// <item>Two instances of the same data store type with the same configuration, and the same prefix,
    /// should be able to see each other's data.</item>
    /// </list>
    /// <para>
    /// You must override the <see cref="Configuration"/> property to provide details specific to
    /// your implementation type.
    /// </para>
    /// </remarks>
    public abstract class PersistentDataStoreBaseTests
    {
        // Note that we don't reference the actual type of the store object within this test code;
        // it can't be provided as a generic type parameter because it is likely to be internal or
        // private, and you can't derive a public test class from a class that has a non-public
        // type parameter. And, it could either be an IPersistentDataStore or an IPersistentDataStoreAsync.
        // So we refer to it as the only lowest common denominator we know: IDisposable.

        /// <summary>
        /// Override this method to create the configuration for the test suite.
        /// </summary>
        protected abstract PersistentDataStoreTestConfig Configuration { get; }

        private readonly TestEntity item1 = new TestEntity("first", 5, "value1");
        private readonly TestEntity item2 = new TestEntity("second", 5, "value2");
        private readonly TestEntity other1 = new TestEntity("third", 5, "othervalue1");
        private readonly string unusedKey = "whatever";
        private readonly ILogAdapter _testLogging;

        protected PersistentDataStoreBaseTests()
        {
            _testLogging = Logs.None;
        }

        protected PersistentDataStoreBaseTests(ITestOutputHelper testOutput)
        {
            _testLogging = TestLogging.TestOutputAdapter(testOutput);
        }

        [Fact]
        public async void StoreNotInitializedBeforeInit()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                Assert.False(await Initialized(store));
            }
        }

        [Fact]
        public async void OneInstanceCanDetectIfAnotherInstanceHasInitializedStore()
        {
            await ClearAllData();
            using (var store1 = CreateStoreImpl())
            {
                await Init(store1, new DataBuilder().Add(TestEntity.Kind, item1).BuildSerialized());

                using (var store2 = CreateStoreImpl())
                {
                    Assert.True(await Initialized(store2));
                }
            }
        }

        [Fact]
        public async void StoreInitializedAfterInit()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().BuildSerialized());
                Assert.True(await Initialized(store));
            }
        }

        [Fact]
        public async void InitCompletelyReplacesExistingData()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                var allData = new DataBuilder()
                     .Add(TestEntity.Kind, item1, item2)
                     .Add(TestEntity.OtherKind, other1)
                     .BuildSerialized();
                await Init(store, allData);

                var item2v2 = item2.NextVersion();
                var data2 = new DataBuilder()
                    .Add(TestEntity.Kind, item2v2)
                    .Add(TestEntity.OtherKind)
                    .BuildSerialized();
                await Init(store, data2);

                Assert.Null(await Get(store, TestEntity.Kind, item1.Key));
                AssertEqualsSerializedItem(item2v2, await Get(store, TestEntity.Kind, item2.Key));
                Assert.Null(await Get(store, TestEntity.OtherKind, other1.Key));
            }
        }

        [Fact]
        public async void GetExistingItem()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                AssertEqualsSerializedItem(item1, await Get(store, TestEntity.Kind, item1.Key));
            }
        }

        [Fact]
        public async void GetNonexistingItem()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                Assert.Null(await Get(store, TestEntity.Kind, unusedKey));
            }
        }

        [Fact]
        public async void GetAllItems()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2)
                .Add(TestEntity.OtherKind, other1).BuildSerialized());
                var result = await GetAll(store, TestEntity.Kind);
                AssertSerializedItemsCollection(result, item1, item2);
            }
        }

        [Fact]
        public async void GetAllWithDeletedItem()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                var deletedItem = new TestEntity(unusedKey, 1, null);
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2, deletedItem)
                    .Add(TestEntity.OtherKind, other1).BuildSerialized());
                var result = await GetAll(store, TestEntity.Kind);
                AssertSerializedItemsCollection(result, item1, item2, deletedItem);
            }
        }

        [Fact]
        public async void UpsertWithNewerVersion()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                var newer = item1.NextVersion();
                await Upsert(store, TestEntity.Kind, item1.Key, newer.SerializedItemDescriptor);
                AssertEqualsSerializedItem(newer, await Get(store, TestEntity.Kind, item1.Key));
            }
        }

        [Fact]
        public async void UpsertWithSameVersion()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                var sameVersionDifferentValue = item1.WithValue("modified");
                await Upsert(store, TestEntity.Kind, item1.Key, sameVersionDifferentValue.SerializedItemDescriptor);
                AssertEqualsSerializedItem(item1, await Get(store, TestEntity.Kind, item1.Key));
            }
        }

        [Fact]
        public async void UpsertWithOlderVersion()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                var older = item1.WithVersion(item1.Version - 1);
                await Upsert(store, TestEntity.Kind, item1.Key, older.SerializedItemDescriptor);
                AssertEqualsSerializedItem(item1, await Get(store, TestEntity.Kind, item1.Key));
            }
        }

        [Fact]
        public async void UpsertNewItem()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                var newItem = new TestEntity(unusedKey, 1, "newvalue");
                await Upsert(store, TestEntity.Kind, unusedKey, newItem.SerializedItemDescriptor);
                AssertEqualsSerializedItem(newItem, await Get(store, TestEntity.Kind, newItem.Key));
            }
        }

        [Fact]
        public async void DeleteWithNewerVersion()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                var deletedItem = new TestEntity(item1.Key, item1.Version + 1, null);
                await Upsert(store, TestEntity.Kind, item1.Key, deletedItem.SerializedItemDescriptor);
                AssertEqualsSerializedItem(deletedItem, await Get(store, TestEntity.Kind, item1.Key));
            }
        }

        [Fact]
        public async void DeleteWithSameVersion()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                var deletedItem = new TestEntity(item1.Key, item1.Version, null);
                await Upsert(store, TestEntity.Kind, item1.Key, deletedItem.SerializedItemDescriptor);
                AssertEqualsSerializedItem(item1, await Get(store, TestEntity.Kind, item1.Key));
            }
        }

        [Fact]
        public async void DeleteWithOlderVersion()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1, item2).BuildSerialized());
                var deletedItem = new TestEntity(item1.Key, item1.Version - 1, null);
                await Upsert(store, TestEntity.Kind, item1.Key, deletedItem.SerializedItemDescriptor);
                AssertEqualsSerializedItem(item1, await Get(store, TestEntity.Kind, item1.Key));
            }
        }

        [Fact]
        public async void DeleteUnknownItem()
        {
            await ClearAllData();
            using (var store = CreateStoreImpl())
            {
                await Init(store, new DataBuilder().Add(TestEntity.Kind, item1).BuildSerialized());
                var deletedItem = new TestEntity(unusedKey, 99, null);
                await Upsert(store, TestEntity.Kind, unusedKey, deletedItem.SerializedItemDescriptor);
                AssertEqualsSerializedItem(deletedItem, await Get(store, TestEntity.Kind, unusedKey));
            }
        }

        [Fact]
        public async void StoresWithDifferentPrefixAreIndependent()
        {
            // The prefix parameter, if supported, is a namespace for all of a store's data,
            // so that it won't interfere with data from some other instance with a different
            // prefix. This test verifies that Init, Get, All, and Upsert are all respecting
            // the prefix.
            await ClearAllData("aaa");
            await ClearAllData("bbb");

            using (var store1 = CreateStoreImpl("aaa"))
            {
                using (var store2 = CreateStoreImpl("bbb"))
                {
                    Assert.False(await Initialized(store1));
                    Assert.False(await Initialized(store2));

                    var store1Item1 = new TestEntity("a", 1, "1a");
                    var store1Item2 = new TestEntity("b", 1, "1b");
                    var store1Item3 = new TestEntity("c", 1, "1c");
                    var store2Item1 = new TestEntity("a", 99, "2a");
                    var store2Item2 = new TestEntity("bb", 1, "2b"); // skipping key "b" validates that store2.Init doesn't delete store1's "b" key
                    var store2Item3 = new TestEntity("c", 2, "2c");
                    await Init(store1, new DataBuilder().Add(TestEntity.Kind, store1Item1, store1Item2).BuildSerialized());
                    await Init(store2, new DataBuilder().Add(TestEntity.Kind, store2Item1, store2Item2).BuildSerialized());
                    await Upsert(store1, TestEntity.Kind, store1Item3.Key, store1Item3.SerializedItemDescriptor);
                    await Upsert(store2, TestEntity.Kind, store2Item3.Key, store2Item3.SerializedItemDescriptor);

                    var items1 = await GetAll(store1, TestEntity.Kind);
                    AssertSerializedItemsCollection(items1, store1Item1, store1Item2, store1Item3);
                    var items2 = await GetAll(store2, TestEntity.Kind);
                    AssertSerializedItemsCollection(items2, store2Item1, store2Item2, store2Item3);
                }
            }
        }

        [Fact]
        public async void UpsertRaceConditionAgainstOtherClientWithLowerVersion()
        {
            if (Configuration.SetConcurrentModificationHookAction is null)
            {
                return;
            }

            var key = "key";
            int startVersion = 1, store2VersionStart = 2, store2VersionEnd = 4, store1VersionEnd = 10;
            var startItem = new TestEntity(key, startVersion, "value1");

            using (var store2 = CreateStoreImpl())
            {
                int versionCounter = store2VersionStart;
                Action concurrentModifier = () =>
                {
                    if (versionCounter <= store2VersionEnd)
                    {
                        AsyncUtils.WaitSafely(() => Upsert(store2, TestEntity.Kind, key,
                            startItem.WithVersion(versionCounter).WithValue("value" + versionCounter).SerializedItemDescriptor));
                        versionCounter++;
                    }
                };

                var store1 = CreateStoreImplWithUpdateHook(concurrentModifier);
                await Init(store1, new DataBuilder().Add(TestEntity.Kind, startItem).BuildSerialized());

                var endItem = startItem.WithVersion(store1VersionEnd).WithValue("value" + store1VersionEnd);
                await Upsert(store1, TestEntity.Kind, key, endItem.SerializedItemDescriptor);

                AssertEqualsSerializedItem(endItem, await Get(store1, TestEntity.Kind, key));
            }
        }
        
        [Fact]
        public async void UpsertRaceConditionAgainstOtherClientWithHigherVersion()
        {
            if (Configuration.SetConcurrentModificationHookAction is null)
            {
                return;
            }

            var key = "key";
            int startVersion = 1, higherVersion = 3, attemptedVersion = 2;
            var startItem = new TestEntity(key, startVersion, "value1");
            var higherItem = startItem.WithVersion(higherVersion).WithValue("value" + higherVersion);

            using (var store2 = CreateStoreImpl())
            {
                Action concurrentModifier = () =>
                {
                    AsyncUtils.WaitSafely(() => Upsert(store2, TestEntity.Kind, key,
                        higherItem.SerializedItemDescriptor));
                };

                var store1 = CreateStoreImplWithUpdateHook(concurrentModifier);
                await Init(store1, new DataBuilder().Add(TestEntity.Kind, startItem).BuildSerialized());

                var attemptedItem = startItem.WithVersion(attemptedVersion);
                await Upsert(store1, TestEntity.Kind, key, attemptedItem.SerializedItemDescriptor);

                AssertEqualsSerializedItem(higherItem, await Get(store1, TestEntity.Kind, key));
            }
        }

        [Fact]
        public void LdClientEndToEndTests()
        {
            // This is a basic smoke test to verify that the data store component behaves correctly within an
            // SDK client instance.

            var flag = FlagTestData.MakeFlagThatReturnsVariationForSegmentMatch(1, FlagTestData.GoodVariation1);
            var segment = FlagTestData.MakeSegmentThatMatchesUserKeys(1, FlagTestData.UserKey);
            var data = FlagTestData.MakeFullDataSet(flag, segment);
            var dataSourceFactory = new TestDataSourceFactory(data);

            var clientConfig = LaunchDarkly.Sdk.Server.Configuration.Builder("sdk-key")
                .DataSource(dataSourceFactory)
                .Events(Components.NoEvents)
                .Logging(Components.Logging(_testLogging));

            if (Configuration.StoreFactoryFunc != null)
            {
                clientConfig.DataStore(Components.PersistentDataStore(Configuration.StoreFactoryFunc(null)));
            }
            else if (Configuration.StoreAsyncFactoryFunc != null)
            {
                clientConfig.DataStore(Components.PersistentDataStore(Configuration.StoreAsyncFactoryFunc(null)));
            }
            else
            {
                throw new InvalidOperationException("neither StoreFactoryFunc nor StoreAsyncFactoryFunc was set");
            }

            using (var client = new LdClient(clientConfig.Build()))
            {
                var dataSourceUpdates = dataSourceFactory._updates;

                Action<Context, LdValue> flagShouldHaveValueForUser = (user, value) =>
                    Assert.Equal(value, client.JsonVariation(FlagTestData.FlagKey, user, LdValue.Null));

                // evaluate each flag from the data store
                flagShouldHaveValueForUser(FlagTestData.MainUser, FlagTestData.GoodValue1);
                flagShouldHaveValueForUser(FlagTestData.OtherUser, FlagTestData.BadValue);

                // evaluate all flags
                var state = client.AllFlagsState(FlagTestData.MainUser);
                Assert.Equal(FlagTestData.GoodValue1, state.GetFlagValueJson(FlagTestData.FlagKey));

                // update the flag
                var flagV2 = FlagTestData.MakeFlagThatReturnsVariationForSegmentMatch(2, FlagTestData.GoodVariation2);
                dataSourceUpdates.Upsert(DataModel.Features, FlagTestData.FlagKey, flagV2);

                // flag should now return new value
                flagShouldHaveValueForUser(FlagTestData.MainUser, FlagTestData.GoodValue2);
                flagShouldHaveValueForUser(FlagTestData.OtherUser, FlagTestData.BadValue);

                // update the segment so it now matches both users
                var segmentV2 = FlagTestData.MakeSegmentThatMatchesUserKeys(2,
                    FlagTestData.UserKey, FlagTestData.OtherUserKey);
                dataSourceUpdates.Upsert(DataModel.Segments, FlagTestData.SegmentKey, segmentV2);

                flagShouldHaveValueForUser(FlagTestData.MainUser, FlagTestData.GoodValue2);
                flagShouldHaveValueForUser(FlagTestData.OtherUser, FlagTestData.GoodValue2);

                // delete the segment - should cause the flag that uses it to stop matching
                dataSourceUpdates.Upsert(DataModel.Segments, FlagTestData.SegmentKey, ItemDescriptor.Deleted(3));
                flagShouldHaveValueForUser(FlagTestData.MainUser, FlagTestData.BadValue);
                flagShouldHaveValueForUser(FlagTestData.OtherUser, FlagTestData.BadValue);

                // delete the flag so it becomes unknown
                dataSourceUpdates.Upsert(DataModel.Features, FlagTestData.FlagKey, ItemDescriptor.Deleted(3));
                var detail = client.JsonVariationDetail(FlagTestData.FlagKey, FlagTestData.MainUser, LdValue.Null);
                Assert.Equal(EvaluationReason.ErrorReason(EvaluationErrorKind.FlagNotFound), detail.Reason);
            }
        }

        private IDisposable CreateStoreImpl(string prefix = null)
        {
            var context = new LdClientContext("sdk-key");
            if (Configuration.StoreFactoryFunc != null)
            {
                return Configuration.StoreFactoryFunc(prefix).Build(context);
            }
            if (Configuration.StoreAsyncFactoryFunc != null)
            {
                return Configuration.StoreAsyncFactoryFunc(prefix).Build(context);
            }
            throw new InvalidOperationException("neither StoreFactoryFunc nor StoreAsyncFactoryFunc was set");
        }

        private IDisposable CreateStoreImplWithUpdateHook(Action hook)
        {
            var store = CreateStoreImpl();
            Configuration.SetConcurrentModificationHookAction(store, hook);
            return store;
        }

        private Task ClearAllData(string prefix = null)
        {
            if (Configuration.ClearDataAction is null)
            {
                throw new InvalidOperationException("configuration did not specify ClearDataAction");
            }
            return Configuration.ClearDataAction(prefix);
        }

        private static async Task<bool> Initialized(IDisposable store)
        {
            if (store is IPersistentDataStore syncStore)
            {
                return syncStore.Initialized();
            }
            return await (store as IPersistentDataStoreAsync).InitializedAsync();
        }

        private static async Task Init(IDisposable store, FullDataSet<SerializedItemDescriptor> allData)
        {
            if (store is IPersistentDataStore syncStore)
            {
                syncStore.Init(allData);
            }
            else
            {
                await (store as IPersistentDataStoreAsync).InitAsync(allData);
            }
        }

        private static async Task<SerializedItemDescriptor?> Get(IDisposable store, DataKind kind, string key)
        {
            if (store is IPersistentDataStore syncStore)
            {
                return syncStore.Get(kind, key);
            }
            return await (store as IPersistentDataStoreAsync).GetAsync(kind, key);
        }

        private static async Task<KeyedItems<SerializedItemDescriptor>> GetAll(IDisposable store, DataKind kind)
        {
            if (store is IPersistentDataStore syncStore)
            {
                return syncStore.GetAll(kind);
            }
            return await (store as IPersistentDataStoreAsync).GetAllAsync(kind);
        }

        private static async Task<bool> Upsert(IDisposable store, DataKind kind, string key, SerializedItemDescriptor item)
        {
            if (store is IPersistentDataStore syncStore)
            {
                return syncStore.Upsert(kind, key, item);
            }
            return await (store as IPersistentDataStoreAsync).UpsertAsync(kind, key, item);
        }

        private static void AssertEqualsSerializedItem(TestEntity item, SerializedItemDescriptor? serializedItemDesc)
        {
            // This allows for the fact that a PersistentDataStore may not be able to get the item version without
            // deserializing it, so we allow the version to be zero. Also, there are two ways a store can return a
            // deleted item, depending on its ability to persist metadata: either Deleted is true, in which case
            // it doesn't matter what SerializedItem is, or else SerializedItem contains whatever placeholder
            // string the DataKind uses to denote deleted items.
            Assert.NotNull(serializedItemDesc);
            if (serializedItemDesc.Value.Version != 0)
            {
                Assert.Equal(item.Version, serializedItemDesc.Value.Version);
            }
            if (serializedItemDesc.Value.Deleted)
            {
                Assert.True(item.Deleted);
            }
            else
            {
                Assert.Equal(item.SerializedItemDescriptor.SerializedItem, serializedItemDesc.Value.SerializedItem);
            }
        }

        private static void AssertSerializedItemsCollection(KeyedItems<SerializedItemDescriptor> serializedItems, params TestEntity[] expectedItems)
        {
            var sortedItems = serializedItems.Items.OrderBy(kv => kv.Key);
            Assert.Collection(sortedItems,
                expectedItems.Select<TestEntity, Action<KeyValuePair<string, SerializedItemDescriptor>>>(item =>
                    kv =>
                    {
                        Assert.Equal(item.Key, kv.Key);
                        AssertEqualsSerializedItem(item, kv.Value);
                    }
                ).ToArray()
                );
        }

        private class TestDataSourceFactory : IComponentConfigurer<IDataSource>
        {
            private readonly FullDataSet<ItemDescriptor> _data;
            internal IDataSourceUpdates _updates;

            internal TestDataSourceFactory(FullDataSet<ItemDescriptor> data)
            {
                _data = data;
            }

            public IDataSource Build(LdClientContext context)
            {
                _updates = context.DataSourceUpdates;
                return new TestDataSource(_data, context.DataSourceUpdates);
            }
        }

        private class TestDataSource : IDataSource
        {
            private readonly FullDataSet<ItemDescriptor> _data;
            private readonly IDataSourceUpdates _updates;

            internal TestDataSource(FullDataSet<ItemDescriptor> data, IDataSourceUpdates updates)
            {
                _data = data;
                _updates = updates;
            }

            public void Dispose() { }

            public bool Initialized => true;

            public Task<bool> Start()
            {
                _updates.Init(_data);
                return Task.FromResult(true);
            }
        }
    }
}
