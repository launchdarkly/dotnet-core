using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Sdk.Server.Internal.DataStores;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Internal.DataStores.DataStoreTestTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    public class WriteThroughStoreTest
    {
        private readonly TestItem _item1 = new TestItem("item1");
        private const string Item1Key = "key1";
        private const int Item1Version = 10;

        private readonly TestItem _item2 = new TestItem("item2");
        private const string Item2Key = "key2";
        private const int Item2Version = 11;

        private FullDataSet<ItemDescriptor> CreateTestDataSet()
        {
            return new TestDataBuilder()
                .Add(TestDataKind, Item1Key, Item1Version, _item1)
                .Add(TestDataKind, Item2Key, Item2Version, _item2)
                .Build();
        }

        private ChangeSet<ItemDescriptor> CreateFullChangeSet()
        {
            var changeSetData = ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    TestDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add(Item1Key, new ItemDescriptor(Item1Version, _item1))
                        .Add(Item2Key, new ItemDescriptor(Item2Version, _item2)))
                )
            );

            return new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full,
                Selector.Make(1, "state1"),
                changeSetData,
                null
            );
        }

        #region Construction Tests

        [Fact]
        public void Constructor_WithPersistence_SetsActiveReadStoreToPersistent()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                persistentStore.SetData(TestDataKind, Item1Key, new ItemDescriptor(Item1Version, _item1));

                var result = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(result);
                Assert.Equal(Item1Version, result.Value.Version);
                Assert.True(persistentStore.WasGetCalled);
            }
        }

        [Fact]
        public void Constructor_WithoutPersistence_SetsActiveReadStoreToMemory()
        {
            var memoryStore = new InMemoryDataStore();

            using (var store =
                   new WriteThroughStore(memoryStore, null, DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                memoryStore.Upsert(TestDataKind, Item1Key, new ItemDescriptor(Item1Version, _item1));

                var result = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(result);
                Assert.Equal(Item1Version, result.Value.Version);
            }
        }

        #endregion

        #region Init Tests

        [Fact]
        public void Init_WithoutPersistence_InitializesMemoryStoreOnly()
        {
            var memoryStore = new InMemoryDataStore();

            using (var store =
                   new WriteThroughStore(memoryStore, null, DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var testData = CreateTestDataSet();
                store.Init(testData);

                Assert.True(memoryStore.Initialized());
                var result = memoryStore.Get(TestDataKind, Item1Key);
                Assert.NotNull(result);
                Assert.Equal(Item1Version, result.Value.Version);
            }
        }

        [Fact]
        public void Init_WithPersistenceReadWrite_InitializesBothStores()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var testData = CreateTestDataSet();
                store.Init(testData);

                Assert.True(memoryStore.Initialized());
                Assert.True(persistentStore.Initialized());
                Assert.True(persistentStore.WasInitCalled);
            }
        }

        [Fact]
        public void Init_WithPersistenceReadOnly_InitializesMemoryStoreOnly()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadOnly))
            {
                var testData = CreateTestDataSet();
                store.Init(testData);

                Assert.True(memoryStore.Initialized());
                Assert.False(persistentStore.WasInitCalled);
            }
        }

        [Fact]
        public void Init_SwitchesActiveReadStoreToMemory()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                persistentStore.SetData(TestDataKind, Item1Key, new ItemDescriptor(5, new TestItem("old")));

                var testData = CreateTestDataSet();
                store.Init(testData);

                var result = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(result);
                Assert.Equal(Item1Version, result.Value.Version);
                Assert.Equal(_item1, result.Value.Item);
            }
        }

        #endregion

        #region Get/GetAll Tests

        [Fact]
        public void Get_BeforeSwitch_ReadsFromPersistentStore()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                persistentStore.SetData(TestDataKind, Item1Key, new ItemDescriptor(Item1Version, _item1));

                var result = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(result);
                Assert.True(persistentStore.WasGetCalled);
            }
        }

        [Fact]
        public void Get_AfterSwitch_ReadsFromMemoryStore()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var testData = CreateTestDataSet();
                store.Init(testData);

                persistentStore.ResetCallTracking();

                var result = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(result);
                Assert.False(persistentStore.WasGetCalled);
            }
        }

        [Fact]
        public void GetAll_AfterSwitch_ReadsFromMemoryStore()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var testData = CreateTestDataSet();
                store.Init(testData);

                persistentStore.ResetCallTracking();

                var result = store.GetAll(TestDataKind);
                Assert.Equal(2, result.Items.Count());
                Assert.False(persistentStore.WasGetAllCalled);
            }
        }

        #endregion

        #region Upsert Tests

        [Fact]
        public void Upsert_WithoutPersistence_UpdatesMemoryStoreOnly()
        {
            var memoryStore = new InMemoryDataStore();

            using (var store =
                   new WriteThroughStore(memoryStore, null, DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var result = store.Upsert(TestDataKind, Item1Key, new ItemDescriptor(Item1Version, _item1));
                Assert.True(result);

                var retrieved = memoryStore.Get(TestDataKind, Item1Key);
                Assert.NotNull(retrieved);
                Assert.Equal(Item1Version, retrieved.Value.Version);
            }
        }

        [Fact]
        public void Upsert_WithPersistenceReadWrite_UpdatesBothStores()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var result = store.Upsert(TestDataKind, Item1Key, new ItemDescriptor(Item1Version, _item1));
                Assert.True(result);
                Assert.True(persistentStore.WasUpsertCalled);

                var retrieved = memoryStore.Get(TestDataKind, Item1Key);
                Assert.NotNull(retrieved);
            }
        }

        [Fact]
        public void Upsert_WithPersistenceReadOnly_UpdatesMemoryStoreOnly()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadOnly))
            {
                var result = store.Upsert(TestDataKind, Item1Key, new ItemDescriptor(Item1Version, _item1));
                Assert.True(result);
                Assert.False(persistentStore.WasUpsertCalled);
            }
        }

        [Fact]
        public void Upsert_WhenPersistentStoreFails_ReturnsFalse()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore { FailUpsert = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var result = store.Upsert(TestDataKind, Item1Key, new ItemDescriptor(Item1Version, _item1));
                Assert.False(result);
            }
        }

        #endregion

        #region Apply Tests

        [Fact]
        public void Apply_WithFullChangeSet_AppliesToBothStores()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockTransactionalPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var changeSet = CreateFullChangeSet();
                store.Apply(changeSet);

                Assert.True(memoryStore.Initialized());
                Assert.True(persistentStore.WasApplyCalled);
            }
        }

        [Fact]
        public void Apply_WithPartialChangeSet_AppliesToBothStores()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockTransactionalPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                store.Init(CreateTestDataSet());

                var item3 = new TestItem("item3");
                var changeSetData = ImmutableList.Create(
                    new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                        TestDataKind,
                        new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                            .Add("key3", new ItemDescriptor(30, item3)))
                    )
                );

                var changeSet = new ChangeSet<ItemDescriptor>(
                    ChangeSetType.Partial,
                    Selector.Make(2, "state2"),
                    changeSetData,
                    null
                );

                persistentStore.ResetCallTracking();
                store.Apply(changeSet);

                Assert.True(persistentStore.WasApplyCalled);
                var result = memoryStore.Get(TestDataKind, "key3");
                Assert.NotNull(result);
                Assert.Equal(item3, result.Value.Item);
            }
        }

        [Fact]
        public void Apply_WithLegacyPersistentStore_FullChangeSet_CallsInit()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var changeSet = CreateFullChangeSet();
                store.Apply(changeSet);

                Assert.True(persistentStore.WasInitCalled);
            }
        }

        [Fact]
        public void Apply_WithLegacyPersistentStore_PartialChangeSet_CallsUpsert()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                store.Init(CreateTestDataSet());

                var item3 = new TestItem("item3");
                var changeSetData = ImmutableList.Create(
                    new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                        TestDataKind,
                        new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                            .Add("key3", new ItemDescriptor(30, item3)))
                    )
                );

                var changeSet = new ChangeSet<ItemDescriptor>(
                    ChangeSetType.Partial,
                    Selector.Make(2, "state2"),
                    changeSetData,
                    null
                );

                persistentStore.ResetCallTracking();
                store.Apply(changeSet);

                Assert.True(persistentStore.WasUpsertCalled);
            }
        }

        [Fact]
        public void Apply_SwitchesActiveReadStoreToMemory()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockTransactionalPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                persistentStore.SetData(TestDataKind, Item1Key, new ItemDescriptor(5, new TestItem("old")));

                var changeSet = CreateFullChangeSet();
                store.Apply(changeSet);

                var result = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(result);
                Assert.Equal(Item1Version, result.Value.Version);
                Assert.Equal(_item1, result.Value.Item);

                persistentStore.ResetCallTracking();
                store.Get(TestDataKind, Item1Key);
                Assert.False(persistentStore.WasGetCalled);
            }
        }

        #endregion

        #region Initialized Tests

        [Fact]
        public void Initialized_WithPersistence_ReturnsPersistentStoreStatus()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                Assert.False(store.Initialized());

                persistentStore.SetInitialized(true);

                Assert.True(store.Initialized());
            }
        }

        [Fact]
        public void Initialized_WithoutPersistence_ReturnsMemoryStoreStatus()
        {
            var memoryStore = new InMemoryDataStore();

            using (var store =
                   new WriteThroughStore(memoryStore, null, DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                Assert.False(store.Initialized());

                memoryStore.Init(CreateTestDataSet());

                Assert.True(store.Initialized());
            }
        }

        #endregion

        #region Store Switching Tests

        [Fact]
        public void StoreSwitching_HappensOnlyOnce()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                persistentStore.SetData(TestDataKind, Item1Key, new ItemDescriptor(5, new TestItem("old")));

                store.Init(CreateTestDataSet());

                var result1 = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(result1);
                Assert.Equal(Item1Version, result1.Value.Version);

                persistentStore.SetData(TestDataKind, Item1Key, new ItemDescriptor(20, new TestItem("newer")));

                var newData = new TestDataBuilder()
                    .Add(TestDataKind, Item1Key, 15, new TestItem("item1-v15"))
                    .Build();
                store.Init(newData);

                var result2 = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(result2);
                Assert.Equal(15, result2.Value.Version);
                Assert.NotEqual(20, result2.Value.Version);
            }
        }

        #endregion

        #region Selector Tests

        [Fact]
        public void Selector_ReturnsMemoryStoreSelector()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockTransactionalPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var changeSet = new ChangeSet<ItemDescriptor>(
                    ChangeSetType.Full,
                    Selector.Make(42, "test-state"),
                    ImmutableList<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>>.Empty,
                    null
                );

                store.Apply(changeSet);

                Assert.Equal(42, store.Selector.Version);
                Assert.Equal("test-state", store.Selector.State);
            }
        }

        #endregion

        #region StatusMonitoringEnabled Tests

        [Fact]
        public void StatusMonitoringEnabled_WithPersistence_ReturnsPersistentStoreValue()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore { StatusMonitoringEnabledValue = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                Assert.True(store.StatusMonitoringEnabled);
            }
        }

        [Fact]
        public void StatusMonitoringEnabled_WithoutPersistence_ReturnsFalse()
        {
            var memoryStore = new InMemoryDataStore();

            using (var store =
                   new WriteThroughStore(memoryStore, null, DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                Assert.False(store.StatusMonitoringEnabled);
            }
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_DisposesBothStores()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            var store = new WriteThroughStore(memoryStore, persistentStore,
                DataSystemConfiguration.DataStoreMode.ReadWrite);

            store.Dispose();

            Assert.True(persistentStore.WasDisposeCalled);
        }

        [Fact]
        public void Dispose_WithoutPersistence_DisposesMemoryStoreOnly()
        {
            var memoryStore = new InMemoryDataStore();

            var store = new WriteThroughStore(memoryStore, null, DataSystemConfiguration.DataStoreMode.ReadWrite);

            store.Dispose();
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void Apply_WithLegacyStore_PartialChangeSet_ThrowsWhenUpsertFails()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore { FailUpsert = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                store.Init(CreateTestDataSet());

                var item3 = new TestItem("item3");
                var changeSetData = ImmutableList.Create(
                    new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                        TestDataKind,
                        new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                            .Add("key3", new ItemDescriptor(30, item3)))
                    )
                );

                var changeSet = new ChangeSet<ItemDescriptor>(
                    ChangeSetType.Partial,
                    Selector.Make(2, "state2"),
                    changeSetData,
                    null
                );

                var exception = Assert.Throws<Exception>(() => store.Apply(changeSet));
                Assert.Equal("Failure to apply data set to persistent store.", exception.Message);
            }
        }

        [Fact]
        public void Apply_WithLegacyStore_PartialChangeSet_ThrowsWhenOneOfMultipleUpsertsFails()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore();

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                store.Init(CreateTestDataSet());

                persistentStore.SetUpsertFailureForKey("key4");

                var item3 = new TestItem("item3");
                var item4 = new TestItem("item4");
                var item5 = new TestItem("item5");
                var changeSetData = ImmutableList.Create(
                    new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                        TestDataKind,
                        new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                            .Add("key3", new ItemDescriptor(30, item3))
                            .Add("key4", new ItemDescriptor(40, item4))
                            .Add("key5", new ItemDescriptor(50, item5)))
                    )
                );

                var changeSet = new ChangeSet<ItemDescriptor>(
                    ChangeSetType.Partial,
                    Selector.Make(2, "state2"),
                    changeSetData,
                    null
                );

                var exception = Assert.Throws<Exception>(() => store.Apply(changeSet));
                Assert.Equal("Failure to apply data set to persistent store.", exception.Message);
            }
        }

        [Fact]
        public void Apply_WithLegacyStore_FullChangeSet_PropagatesInitException()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore { ThrowOnInit = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var changeSet = CreateFullChangeSet();

                var exception = Assert.Throws<InvalidOperationException>(() => store.Apply(changeSet));
                Assert.Equal("Init failed", exception.Message);
            }
        }

        [Fact]
        public void Apply_WithTransactionalStore_PropagatesApplyException()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockTransactionalPersistentStore { ThrowOnApply = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                var changeSet = CreateFullChangeSet();

                var exception = Assert.Throws<InvalidOperationException>(() => store.Apply(changeSet));
                Assert.Equal("Apply failed", exception.Message);
            }
        }

        [Fact]
        public void Apply_WithLegacyStore_PartialChangeSet_MemoryStoreStillUpdatedBeforeException()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore { FailUpsert = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                store.Init(CreateTestDataSet());

                var item3 = new TestItem("item3");
                var changeSetData = ImmutableList.Create(
                    new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                        TestDataKind,
                        new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                            .Add("key3", new ItemDescriptor(30, item3)))
                    )
                );

                var changeSet = new ChangeSet<ItemDescriptor>(
                    ChangeSetType.Partial,
                    Selector.Make(2, "state2"),
                    changeSetData,
                    null
                );

                Assert.Throws<Exception>(() => store.Apply(changeSet));

                var result = memoryStore.Get(TestDataKind, "key3");
                Assert.NotNull(result);
                Assert.Equal(item3, result.Value.Item);
            }
        }

        [Fact]
        public void Apply_WithLegacyStore_NoneChangeSet_DoesNotThrow()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore { FailUpsert = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                store.Init(CreateTestDataSet());

                var changeSet = new ChangeSet<ItemDescriptor>(
                    ChangeSetType.None,
                    Selector.Make(2, "state2"),
                    ImmutableList<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>>.Empty,
                    null
                );

                store.Apply(changeSet);
            }
        }

        [Fact]
        public void Apply_SwitchesToMemoryStore_EvenWhenPersistentStoreApplyFails()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockPersistentStore { FailUpsert = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                // Set up persistent store with old data
                persistentStore.SetData(TestDataKind, Item1Key, new ItemDescriptor(5, new TestItem("old")));

                // Before apply, reads should come from persistent store
                var resultBefore = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(resultBefore);
                Assert.Equal(5, resultBefore.Value.Version);
                Assert.Equal(new TestItem("old"), resultBefore.Value.Item);

                var item3 = new TestItem("item3");
                var changeSetData = ImmutableList.Create(
                    new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                        TestDataKind,
                        new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                            .Add("key3", new ItemDescriptor(30, item3)))
                    )
                );

                var changeSet = new ChangeSet<ItemDescriptor>(
                    ChangeSetType.Partial,
                    Selector.Make(2, "state2"),
                    changeSetData,
                    null
                );

                // Apply should throw due to persistent store failure
                Assert.Throws<Exception>(() => store.Apply(changeSet));

                // After apply, even though it failed for persistence, we should have switched to memory store
                // Memory store should have the new data
                var resultAfter = store.Get(TestDataKind, "key3");
                Assert.NotNull(resultAfter);
                Assert.Equal(item3, resultAfter.Value.Item);

                // Verify we're reading from memory, not persistent store
                persistentStore.ResetCallTracking();
                store.Get(TestDataKind, "key3");
                Assert.False(persistentStore.WasGetCalled);
            }
        }

        [Fact]
        public void Apply_SwitchesToMemoryStore_EvenWhenTransactionalStoreApplyFails()
        {
            var memoryStore = new InMemoryDataStore();
            var persistentStore = new MockTransactionalPersistentStore { ThrowOnApply = true };

            using (var store = new WriteThroughStore(memoryStore, persistentStore,
                       DataSystemConfiguration.DataStoreMode.ReadWrite))
            {
                // Set up persistent store with old data
                persistentStore.SetData(TestDataKind, Item1Key, new ItemDescriptor(5, new TestItem("old")));

                // Before apply, reads should come from persistent store
                var resultBefore = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(resultBefore);
                Assert.Equal(5, resultBefore.Value.Version);

                var changeSet = CreateFullChangeSet();

                // Apply should throw due to persistent store failure
                Assert.Throws<InvalidOperationException>(() => store.Apply(changeSet));

                // After apply, even though it failed for persistence, we should have switched to memory store
                // Memory store should have the new data
                var resultAfter = store.Get(TestDataKind, Item1Key);
                Assert.NotNull(resultAfter);
                Assert.Equal(Item1Version, resultAfter.Value.Version);
                Assert.Equal(_item1, resultAfter.Value.Item);

                // Verify we're reading from memory, not persistent store
                persistentStore.ResetCallTracking();
                store.Get(TestDataKind, Item1Key);
                Assert.False(persistentStore.WasGetCalled);
            }
        }

        #endregion

        #region Mock Stores

        private class MockPersistentStore : IDataStore
        {
            private readonly Dictionary<DataKind, Dictionary<string, ItemDescriptor>> _data =
                new Dictionary<DataKind, Dictionary<string, ItemDescriptor>>();

            private readonly HashSet<string> _keysToFailOn = new HashSet<string>();
            private bool _initialized;

            public bool WasInitCalled { get; private set; }
            public bool WasGetCalled { get; private set; }
            public bool WasGetAllCalled { get; private set; }
            public bool WasUpsertCalled { get; private set; }
            public bool WasDisposeCalled { get; private set; }
            public bool FailUpsert { get; set; }
            public bool ThrowOnInit { get; set; }
            public bool StatusMonitoringEnabledValue { get; set; }

            public void SetUpsertFailureForKey(string key)
            {
                _keysToFailOn.Add(key);
            }

            public void ResetCallTracking()
            {
                WasInitCalled = false;
                WasGetCalled = false;
                WasGetAllCalled = false;
                WasUpsertCalled = false;
                WasDisposeCalled = false;
            }

            public void SetData(DataKind kind, string key, ItemDescriptor item)
            {
                if (!_data.ContainsKey(kind))
                {
                    _data[kind] = new Dictionary<string, ItemDescriptor>();
                }

                _data[kind][key] = item;
            }

            public void SetInitialized(bool value)
            {
                _initialized = value;
            }

            public void Init(FullDataSet<ItemDescriptor> allData)
            {
                WasInitCalled = true;
                if (ThrowOnInit)
                {
                    throw new InvalidOperationException("Init failed");
                }

                _data.Clear();
                foreach (var kindData in allData.Data)
                {
                    _data[kindData.Key] = new Dictionary<string, ItemDescriptor>();
                    foreach (var item in kindData.Value.Items)
                    {
                        _data[kindData.Key][item.Key] = item.Value;
                    }
                }

                _initialized = true;
            }

            public ItemDescriptor? Get(DataKind kind, string key)
            {
                WasGetCalled = true;
                if (_data.TryGetValue(kind, out var kindData) && kindData.TryGetValue(key, out var item))
                {
                    return item;
                }

                return null;
            }

            public KeyedItems<ItemDescriptor> GetAll(DataKind kind)
            {
                WasGetAllCalled = true;
                if (_data.TryGetValue(kind, out var kindData))
                {
                    return new KeyedItems<ItemDescriptor>(kindData.ToImmutableDictionary());
                }

                return new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty);
            }

            public bool Upsert(DataKind kind, string key, ItemDescriptor item)
            {
                WasUpsertCalled = true;
                if (FailUpsert || _keysToFailOn.Contains(key))
                {
                    return false;
                }

                if (!_data.ContainsKey(kind))
                {
                    _data[kind] = new Dictionary<string, ItemDescriptor>();
                }

                if (_data[kind].TryGetValue(key, out var existing))
                {
                    if (item.Version <= existing.Version)
                    {
                        return false;
                    }
                }

                _data[kind][key] = item;
                return true;
            }

            public bool Initialized()
            {
                return _initialized;
            }

            public bool StatusMonitoringEnabled => StatusMonitoringEnabledValue;

            public void Dispose()
            {
                WasDisposeCalled = true;
            }
        }

        private class MockTransactionalPersistentStore : MockPersistentStore, ITransactionalDataStore
        {
            public bool WasApplyCalled { get; private set; }
            public bool ThrowOnApply { get; set; }

            public new void ResetCallTracking()
            {
                base.ResetCallTracking();
                WasApplyCalled = false;
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                WasApplyCalled = true;
                if (ThrowOnApply)
                {
                    throw new InvalidOperationException("Apply failed");
                }

                switch (changeSet.Type)
                {
                    case ChangeSetType.Full:
                        Init(new FullDataSet<ItemDescriptor>(changeSet.Data));
                        break;
                    case ChangeSetType.Partial:
                        foreach (var kindData in changeSet.Data)
                        {
                            foreach (var item in kindData.Value.Items)
                            {
                                Upsert(kindData.Key, item.Key, item.Value);
                            }
                        }

                        break;
                }
            }

            public Selector Selector => Selector.Empty;
        }

        #endregion
    }
}
