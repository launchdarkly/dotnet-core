using System.Linq;
using Xunit;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Internal.DataStores.DataStoreTestTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    /// <summary>
    /// Tests for the PersistentDataStoreConverter utility class that converts between
    /// in-memory ItemDescriptor and serialized SerializedItemDescriptor formats.
    /// </summary>
    public class PersistentDataStoreConverterTest
    {
        [Fact]
        public void ToSerializedFormat_ConvertsCorrectly()
        {
            var item1 = new TestItem("item1");
            var item2 = new TestItem("item2");

            var inMemoryData = new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(TestDataKind, "key2", 2, item2)
                .Build();

            var serializedData = PersistentDataStoreConverter.ToSerializedFormat(inMemoryData);

            // Should have one data kind
            Assert.Single(serializedData.Data);

            var testKindData = serializedData.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Equal(2, testKindData.Items.Count());

            // Verify first item
            var serializedItem1 = testKindData.Items.First(kv => kv.Key == "key1").Value;
            Assert.Equal(1, serializedItem1.Version);
            Assert.False(serializedItem1.Deleted);
            Assert.NotNull(serializedItem1.SerializedItem);
            Assert.Equal("item1:1", serializedItem1.SerializedItem);

            // Verify second item
            var serializedItem2 = testKindData.Items.First(kv => kv.Key == "key2").Value;
            Assert.Equal(2, serializedItem2.Version);
            Assert.False(serializedItem2.Deleted);
            Assert.NotNull(serializedItem2.SerializedItem);
            Assert.Equal("item2:2", serializedItem2.SerializedItem);
        }

        [Fact]
        public void ToSerializedFormat_HandlesDeletedItems()
        {
            var item1 = new TestItem("item1");

            var inMemoryData = new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(TestDataKind, "key2", 2, null) // Deleted item
                .Build();

            var serializedData = PersistentDataStoreConverter.ToSerializedFormat(inMemoryData);

            var testKindData = serializedData.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Equal(2, testKindData.Items.Count());

            // Regular item
            var serializedItem1 = testKindData.Items.First(kv => kv.Key == "key1").Value;
            Assert.Equal(1, serializedItem1.Version);
            Assert.False(serializedItem1.Deleted);

            // Deleted item
            var serializedItem2 = testKindData.Items.First(kv => kv.Key == "key2").Value;
            Assert.Equal(2, serializedItem2.Version);
            Assert.True(serializedItem2.Deleted);
            // Serialized representation still contains the placeholder
            Assert.Equal("DELETED:2", serializedItem2.SerializedItem);
        }

        [Fact]
        public void ToSerializedFormat_PreservesAllDataKinds()
        {
            var item1 = new TestItem("item1");
            var item2 = new TestItem("item2");

            var inMemoryData = new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(OtherDataKind, "key2", 2, item2)
                .Build();

            var serializedData = PersistentDataStoreConverter.ToSerializedFormat(inMemoryData);

            // Should have both data kinds
            Assert.Equal(2, serializedData.Data.Count());

            // Verify TestDataKind
            var testKindData = serializedData.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Single(testKindData.Items);
            Assert.Equal("item1:1", testKindData.Items.First().Value.SerializedItem);

            // Verify OtherDataKind
            var otherKindData = serializedData.Data.First(kv => kv.Key == OtherDataKind).Value;
            Assert.Single(otherKindData.Items);
            Assert.Equal("item2:2", otherKindData.Items.First().Value.SerializedItem);
        }

        [Fact]
        public void ToSerializedFormat_WithEmptyData_ReturnsEmptyDataSet()
        {
            var inMemoryData = new TestDataBuilder().Build();

            var serializedData = PersistentDataStoreConverter.ToSerializedFormat(inMemoryData);

            Assert.Empty(serializedData.Data);
        }

        [Fact]
        public void ToSerializedFormat_WithEmptyKind_IncludesEmptyKind()
        {
            // Create a data set with a kind that has no items
            var builder = new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, new TestItem("item1"));

            var inMemoryData = builder.Build();

            var serializedData = PersistentDataStoreConverter.ToSerializedFormat(inMemoryData);

            // Should have the kind with items
            Assert.Single(serializedData.Data);
            var testKindData = serializedData.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Single(testKindData.Items);
        }

        [Fact]
        public void ToSerializedFormat_PreservesVersionNumbers()
        {
            var item1 = new TestItem("item1");

            var inMemoryData = new TestDataBuilder()
                .Add(TestDataKind, "key1", 100, item1)
                .Add(TestDataKind, "key2", 999, item1)
                .Build();

            var serializedData = PersistentDataStoreConverter.ToSerializedFormat(inMemoryData);

            var testKindData = serializedData.Data.First(kv => kv.Key == TestDataKind).Value;

            var item1Serialized = testKindData.Items.First(kv => kv.Key == "key1").Value;
            Assert.Equal(100, item1Serialized.Version);

            var item2Serialized = testKindData.Items.First(kv => kv.Key == "key2").Value;
            Assert.Equal(999, item2Serialized.Version);
        }

        [Fact]
        public void ToSerializedFormat_WithMultipleItemsInSameKind()
        {
            var item1 = new TestItem("item1");
            var item2 = new TestItem("item2");
            var item3 = new TestItem("item3");

            var inMemoryData = new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(TestDataKind, "key2", 2, item2)
                .Add(TestDataKind, "key3", 3, item3)
                .Build();

            var serializedData = PersistentDataStoreConverter.ToSerializedFormat(inMemoryData);

            var testKindData = serializedData.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Equal(3, testKindData.Items.Count());

            // Verify all three items are present with correct serialization
            Assert.Contains(testKindData.Items, kv => kv.Key == "key1" && kv.Value.SerializedItem == "item1:1");
            Assert.Contains(testKindData.Items, kv => kv.Key == "key2" && kv.Value.SerializedItem == "item2:2");
            Assert.Contains(testKindData.Items, kv => kv.Key == "key3" && kv.Value.SerializedItem == "item3:3");
        }

        [Fact]
        public void ToSerializedFormat_WithMixedDeletedAndRegularItems()
        {
            var item1 = new TestItem("item1");
            var item3 = new TestItem("item3");

            var inMemoryData = new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(TestDataKind, "key2", 2, null) // Deleted
                .Add(TestDataKind, "key3", 3, item3)
                .Add(TestDataKind, "key4", 4, null) // Deleted
                .Build();

            var serializedData = PersistentDataStoreConverter.ToSerializedFormat(inMemoryData);

            var testKindData = serializedData.Data.First(kv => kv.Key == TestDataKind).Value;
            Assert.Equal(4, testKindData.Items.Count());

            // Count deleted vs non-deleted
            var deletedCount = testKindData.Items.Count(kv => kv.Value.Deleted);
            var regularCount = testKindData.Items.Count(kv => !kv.Value.Deleted);

            Assert.Equal(2, deletedCount);
            Assert.Equal(2, regularCount);
        }
    }
}
