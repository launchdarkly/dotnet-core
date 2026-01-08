using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Internal.DataStores.DataStoreTestTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    public class InMemoryDataStoreTest : DataStoreTestBase
    {
        public InMemoryDataStoreTest()
        {
            store = new InMemoryDataStore();
        }

        private InMemoryDataStore TypedStore => (InMemoryDataStore)store;

        #region Apply method tests

        [Fact]
        public void Apply_WithFullChangeSet_ReplacesAllData()
        {
            // Initialize store with some data
            InitStore();

            // Create a full changeset with different data
            var item3 = new TestItem("item3");
            const string item3Key = "key3";
            const int item3Version = 20;

            var changeSetData = ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    TestDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add(item3Key, new ItemDescriptor(item3Version, item3)))
                )
            );

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full,
                Selector.Make(1, "state1"),
                changeSetData,
                "test-env"
            );

            TypedStore.Apply(changeSet);

            // Old items should be gone
            Assert.Null(store.Get(TestDataKind, item1Key));
            Assert.Null(store.Get(TestDataKind, item2Key));

            // New item should exist
            var result = store.Get(TestDataKind, item3Key);
            Assert.NotNull(result);
            Assert.Equal(item3Version, result.Value.Version);
            Assert.Equal(item3, result.Value.Item);
        }

        [Fact]
        public void Apply_WithFullChangeSet_SetsSelector()
        {
            var selector = Selector.Make(42, "test-state");
            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full,
                selector,
                ImmutableList<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>>.Empty,
                null
            );

            TypedStore.Apply(changeSet);

            Assert.Equal(selector.Version, TypedStore.Selector.Version);
            Assert.Equal(selector.State, TypedStore.Selector.State);
        }

        [Fact]
        public void Apply_WithFullChangeSet_MarksStoreAsInitialized()
        {
            Assert.False(store.Initialized());

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full,
                Selector.Make(1, "state1"),
                ImmutableList<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>>.Empty,
                null
            );

            TypedStore.Apply(changeSet);

            Assert.True(store.Initialized());
        }

        [Fact]
        public void Apply_WithPartialChangeSet_AddsNewItems()
        {
            InitStore();

            var item3 = new TestItem("item3");
            const string item3Key = "key3";
            const int item3Version = 20;

            var changeSetData = ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    TestDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add(item3Key, new ItemDescriptor(item3Version, item3)))
                )
            );

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Partial,
                Selector.Make(2, "state2"),
                changeSetData,
                null
            );

            TypedStore.Apply(changeSet);

            // Old items should still exist
            Assert.NotNull(store.Get(TestDataKind, item1Key));
            Assert.NotNull(store.Get(TestDataKind, item2Key));

            // New item should exist
            var result = store.Get(TestDataKind, item3Key);
            Assert.NotNull(result);
            Assert.Equal(item3Version, result.Value.Version);
            Assert.Equal(item3, result.Value.Item);
        }

        [Fact]
        public void Apply_WithPartialChangeSet_CanReplaceItems()
        {
            InitStore();

            // Partial updates replace the entire data kind with the provided items
            var item1Updated = new TestItem("item1-updated");
            const int item1NewVersion = item1Version + 10;

            var changeSetData = ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    TestDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add(item1Key, new ItemDescriptor(item1NewVersion, item1Updated))
                        .Add(item2Key, new ItemDescriptor(item2Version, item2))) // Must include all items in kind
                )
            );

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Partial,
                Selector.Make(2, "state2"),
                changeSetData,
                null
            );

            TypedStore.Apply(changeSet);

            // Item should be updated
            var result = store.Get(TestDataKind, item1Key);
            Assert.NotNull(result);
            Assert.Equal(item1NewVersion, result.Value.Version);
            Assert.Equal(item1Updated, result.Value.Item);

            // The other item should still exist
            var result2 = store.Get(TestDataKind, item2Key);
            Assert.NotNull(result2);
            Assert.Equal(item2Version, result2.Value.Version);
            Assert.Equal(item2, result2.Value.Item);
        }

        [Fact]
        public void Apply_WithPartialChangeSet_CanDeleteItems()
        {
            InitStore();

            // When applying partial changeset, include deleted item and keep other items
            var changeSetData = ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    TestDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add(item1Key, ItemDescriptor.Deleted(item1Version + 10))
                        .Add(item2Key, new ItemDescriptor(item2Version, item2))) // Must include all items in kind
                )
            );

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Partial,
                Selector.Make(2, "state2"),
                changeSetData,
                null
            );

            TypedStore.Apply(changeSet);

            // Item should be marked as deleted
            var result = store.Get(TestDataKind, item1Key);
            Assert.NotNull(result);
            Assert.Null(result.Value.Item);
            Assert.Equal(item1Version + 10, result.Value.Version);

            // The other item should still exist
            var result2 = store.Get(TestDataKind, item2Key);
            Assert.NotNull(result2);
            Assert.Equal(item2Version, result2.Value.Version);
            Assert.Equal(item2, result2.Value.Item);
        }

        [Fact]
        public void Apply_WithPartialChangeSet_UpdatesSelector()
        {
            InitStore();
            var initialSelector = TypedStore.Selector;

            var newSelector = Selector.Make(99, "new-state");
            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Partial,
                newSelector,
                ImmutableList<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>>.Empty,
                null
            );

            TypedStore.Apply(changeSet);

            Assert.NotEqual(initialSelector.Version, TypedStore.Selector.Version);
            Assert.Equal(newSelector.Version, TypedStore.Selector.Version);
            Assert.Equal(newSelector.State, TypedStore.Selector.State);
        }

        [Fact]
        public void Apply_WithNoneChangeSet_DoesNotModifyData()
        {
            InitStore();

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.None,
                Selector.Make(5, "state5"),
                ImmutableList<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>>.Empty,
                null
            );

            TypedStore.Apply(changeSet);

            // Data should remain unchanged
            var result1 = store.Get(TestDataKind, item1Key);
            Assert.NotNull(result1);
            Assert.Equal(item1Version, result1.Value.Version);
            Assert.Equal(item1, result1.Value.Item);

            var result2 = store.Get(TestDataKind, item2Key);
            Assert.NotNull(result2);
            Assert.Equal(item2Version, result2.Value.Version);
            Assert.Equal(item2, result2.Value.Item);
        }

        [Fact]
        public void Apply_WithFullChangeSet_HandlesMultipleDataKinds()
        {
            var updatedItem1 = new TestItem("item1");
            var updatedItem2 = new TestItem("item2");

            var changeSetData = ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    TestDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add("key1", new ItemDescriptor(1, updatedItem1)))
                ),
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    OtherDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add("key2", new ItemDescriptor(2, updatedItem2)))
                )
            );

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full,
                Selector.Make(1, "state1"),
                changeSetData,
                null
            );

            TypedStore.Apply(changeSet);

            // Both kinds should have data
            var result1 = store.Get(TestDataKind, "key1");
            Assert.NotNull(result1);
            Assert.Equal(updatedItem1, result1.Value.Item);

            var result2 = store.Get(OtherDataKind, "key2");
            Assert.NotNull(result2);
            Assert.Equal(updatedItem2, result2.Value.Item);
        }

        [Fact]
        public void Apply_WithPartialChangeSet_HandlesMultipleDataKinds()
        {
            InitStore();

            var item3 = new TestItem("item3");
            var item4 = new TestItem("item4");

            var changeSetData = ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    TestDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add("key3", new ItemDescriptor(30, item3)))
                ),
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    OtherDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add("key4", new ItemDescriptor(40, item4)))
                )
            );

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Partial,
                Selector.Make(2, "state2"),
                changeSetData,
                null
            );

            TypedStore.Apply(changeSet);

            // Original TestDataKind items should still exist
            Assert.NotNull(store.Get(TestDataKind, item1Key));
            Assert.NotNull(store.Get(TestDataKind, item2Key));

            // New items should exist
            var result3 = store.Get(TestDataKind, "key3");
            Assert.NotNull(result3);
            Assert.Equal(item3, result3.Value.Item);

            var result4 = store.Get(OtherDataKind, "key4");
            Assert.NotNull(result4);
            Assert.Equal(item4, result4.Value.Item);
        }

        [Fact]
        public void Apply_WithPartialChangeSet_PreservesUnaffectedDataKinds()
        {
            // Initialize with both TestDataKind and OtherDataKind
            var allData = new TestDataBuilder()
                .Add(TestDataKind, item1Key, item1Version, item1)
                .Add(TestDataKind, item2Key, item2Version, item2)
                .Add(OtherDataKind, "other1", 100, new TestItem("other1"))
                .Build();
            store.Init(allData);

            // Apply partial changeset that only updates TestDataKind
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

            TypedStore.Apply(changeSet);

            // TestDataKind should have original items plus new item
            Assert.NotNull(store.Get(TestDataKind, item1Key));
            Assert.NotNull(store.Get(TestDataKind, item2Key));
            Assert.NotNull(store.Get(TestDataKind, "key3"));

            // OtherDataKind should still exist and be unchanged
            var otherResult = store.Get(OtherDataKind, "other1");
            Assert.NotNull(otherResult);
            Assert.Equal(new TestItem("other1"), otherResult.Value.Item);
            Assert.Equal(100, otherResult.Value.Version);
        }

        [Fact]
        public void Apply_WithFullChangeSet_EmptyData_ClearsStore()
        {
            InitStore();

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full,
                Selector.Make(1, "state1"),
                ImmutableList<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>>.Empty,
                null
            );

            TypedStore.Apply(changeSet);

            // All items should be gone
            Assert.Null(store.Get(TestDataKind, item1Key));
            Assert.Null(store.Get(TestDataKind, item2Key));

            // But store should be initialized
            Assert.True(store.Initialized());
        }

        [Fact]
        public void Apply_WithPartialChangeSet_OnUninitializedStore()
        {
            Assert.False(store.Initialized());

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
                Selector.Make(1, "state1"),
                changeSetData,
                null
            );

            TypedStore.Apply(changeSet);

            // Item should be added
            var result = store.Get(TestDataKind, "key3");
            Assert.NotNull(result);
            Assert.Equal(item3, result.Value.Item);

            // Store should still not be marked as initialized (partial updates don't initialize)
            Assert.False(store.Initialized());
        }

        [Fact]
        public void Apply_WithFullChangeSet_SetsEnvironmentIdInMetadata()
        {
            const string environmentId = "test-environment-123";
            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full,
                Selector.Make(1, "state1"),
                ImmutableList<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>>.Empty,
                environmentId
            );

            TypedStore.Apply(changeSet);

            var metadata = TypedStore.GetMetadata();
            Assert.NotNull(metadata);
            Assert.Equal(environmentId, metadata.EnvironmentId);
        }

        [Fact]
        public void Apply_WithMultipleItemsInSameKind()
        {
            var localItem1 = new TestItem("item1");
            var localItem2 = new TestItem("item2");
            var item3 = new TestItem("item3");

            var changeSetData = ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                    TestDataKind,
                    new KeyedItems<ItemDescriptor>(ImmutableDictionary<string, ItemDescriptor>.Empty
                        .Add("key1", new ItemDescriptor(10, localItem1))
                        .Add("key2", new ItemDescriptor(20, localItem2))
                        .Add("key3", new ItemDescriptor(30, item3)))
                )
            );

            var changeSet = new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full,
                Selector.Make(1, "state1"),
                changeSetData,
                null
            );

            TypedStore.Apply(changeSet);

            var allItems = store.GetAll(TestDataKind);
            Assert.Equal(3, allItems.Items.Count());
        }

        #endregion

        #region ExportAllData tests

        [Fact]
        public void ExportAllData_ReturnsCompleteSnapshot()
        {
            var item1 = new TestItem("item1");
            var item2 = new TestItem("item2");
            var item3 = new TestItem("item3");

            store.Init(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(TestDataKind, "key2", 2, item2)
                .Add(OtherDataKind, "key3", 3, item3)
                .Build());

            var exported = TypedStore.ExportAll();

            // Should have both data kinds
            Assert.Equal(2, exported.Data.Count());

            // Verify TestDataKind data
            var testKindData = exported.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Equal(2, testKindData.Items.Count());
            Assert.Equal(item1, testKindData.Items.First(kv => kv.Key == "key1").Value.Item);
            Assert.Equal(1, testKindData.Items.First(kv => kv.Key == "key1").Value.Version);
            Assert.Equal(item2, testKindData.Items.First(kv => kv.Key == "key2").Value.Item);
            Assert.Equal(2, testKindData.Items.First(kv => kv.Key == "key2").Value.Version);

            // Verify OtherDataKind data
            var otherKindData = exported.Data.First(kv => kv.Key == OtherDataKind).Value;
            Assert.Single(otherKindData.Items);
            Assert.Equal(item3, otherKindData.Items.First().Value.Item);
            Assert.Equal(3, otherKindData.Items.First().Value.Version);
        }

        [Fact]
        public void ExportAllData_WithEmptyStore_ReturnsEmptyDataSet()
        {
            var exported = TypedStore.ExportAll();

            Assert.Empty(exported.Data);
        }

        [Fact]
        public void ExportAllData_WithDeletedItems_IncludesDeletedItems()
        {
            var item1 = new TestItem("item1");

            store.Init(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(TestDataKind, "key2", 2, null) // Deleted item
                .Build());

            var exported = TypedStore.ExportAll();

            var testKindData = exported.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Equal(2, testKindData.Items.Count());

            // Regular item
            var regularItem = testKindData.Items.First(kv => kv.Key == "key1").Value;
            Assert.Equal(item1, regularItem.Item);
            Assert.Equal(1, regularItem.Version);

            // Deleted item
            var deletedItem = testKindData.Items.First(kv => kv.Key == "key2").Value;
            Assert.Null(deletedItem.Item);
            Assert.Equal(2, deletedItem.Version);
        }

        [Fact]
        public void ExportAllData_IsThreadSafe()
        {
            // Initialize with some data
            var item1 = new TestItem("item1");
            store.Init(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Build());

            // Start export in background thread
            System.Threading.Tasks.Task<FullDataSet<ItemDescriptor>> exportTask = null;
            var exportStarted = new System.Threading.ManualResetEventSlim(false);
            var continueExport = new System.Threading.ManualResetEventSlim(false);

            exportTask = System.Threading.Tasks.Task.Run(() =>
            {
                exportStarted.Set();
                continueExport.Wait();
                return TypedStore.ExportAll();
            });

            // Wait for export to start
            exportStarted.Wait();

            // Try to perform concurrent upsert (should block until export completes)
            var item2 = new TestItem("item2");
            var upsertTask = System.Threading.Tasks.Task.Run(() =>
            {
                store.Upsert(TestDataKind, "key2", new ItemDescriptor(2, item2));
            });

            // Let export continue
            continueExport.Set();

            // Wait for both operations to complete
            var exported = exportTask.Result;
            upsertTask.Wait();

            // Export should have completed successfully (no need to assert on value type)

            // Either key2 is in the export (upsert happened before export) or not (upsert happened after)
            // Both are valid outcomes as long as no exception was thrown
            var testKindEntry = exported.Data.FirstOrDefault(kv => kv.Key == TestDataKind);
            if (testKindEntry.Value.Items != null && testKindEntry.Value.Items.Any())
            {
                Assert.Contains(testKindEntry.Value.Items, kv => kv.Key == "key1");
                // key2 may or may not be present depending on timing
            }

            // Verify store now has both items
            Assert.Equal(item1, store.Get(TestDataKind, "key1").Value.Item);
            Assert.Equal(item2, store.Get(TestDataKind, "key2").Value.Item);
        }

        [Fact]
        public void ExportAllData_ReturnsImmutableSnapshot()
        {
            var item1 = new TestItem("item1");
            store.Init(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Build());

            var exported = TypedStore.ExportAll();

            // Modify store after export
            var item2 = new TestItem("item2");
            store.Upsert(TestDataKind, "key2", new ItemDescriptor(2, item2));

            // Exported data should not be affected
            var testKindData = exported.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Single(testKindData.Items);
            Assert.Equal("key1", testKindData.Items.First().Key);
        }

        #endregion
    }
}
