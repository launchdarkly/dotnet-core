using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Internal.DataStores.DataStoreTestTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    public class DataSourceUpdatesV2ToV1AdapterTest
    {
        private readonly CapturingDataSourceUpdatesWithHeaders _sink = new CapturingDataSourceUpdatesWithHeaders();

        private static FullDataSet<ItemDescriptor> MakeDataSet() =>
            new TestDataBuilder().Add(TestDataKind, "key1", 1, new TestItem("item1")).Build();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InitCreatesFullChangeSetWithTheSourceFlag(bool shouldPersist)
        {
            var adapter = new DataSourceUpdatesV2ToV1Adapter(_sink, shouldPersist);

            adapter.Init(MakeDataSet());

            var changeSet = _sink.Applies.ExpectValue();
            Assert.Equal(ChangeSetType.Full, changeSet.Type);
            Assert.Equal(shouldPersist, changeSet.ShouldPersist);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InitWithHeadersCreatesFullChangeSetWithTheSourceFlag(bool shouldPersist)
        {
            var adapter = new DataSourceUpdatesV2ToV1Adapter(_sink, shouldPersist);

            adapter.InitWithHeaders(MakeDataSet(), new List<KeyValuePair<string, IEnumerable<string>>>());

            var changeSet = _sink.Applies.ExpectValue();
            Assert.Equal(ChangeSetType.Full, changeSet.Type);
            Assert.Equal(shouldPersist, changeSet.ShouldPersist);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void UpsertCreatesPartialChangeSetWithTheSourceFlag(bool shouldPersist)
        {
            var adapter = new DataSourceUpdatesV2ToV1Adapter(_sink, shouldPersist);

            adapter.Upsert(TestDataKind, "key1", new ItemDescriptor(2, new TestItem("item1")));

            var changeSet = _sink.Applies.ExpectValue();
            Assert.Equal(ChangeSetType.Partial, changeSet.Type);
            Assert.Equal(shouldPersist, changeSet.ShouldPersist);
        }

        [Fact]
        public void FlagDefaultsToPersist()
        {
            var adapter = new DataSourceUpdatesV2ToV1Adapter(_sink);

            adapter.Init(MakeDataSet());
            adapter.Upsert(TestDataKind, "key1", new ItemDescriptor(2, new TestItem("item1")));

            Assert.True(_sink.Applies.ExpectValue().ShouldPersist);
            Assert.True(_sink.Applies.ExpectValue().ShouldPersist);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ApplyPassesChangeSetThroughWithItsOwnFlag(bool changeSetShouldPersist)
        {
            // An FDv2 source applies its change sets directly; the adapter does not change their flag.
            var adapter = new DataSourceUpdatesV2ToV1Adapter(_sink, !changeSetShouldPersist);
            var data = ImmutableList.Create(new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                TestDataKind, KeyedItems<ItemDescriptor>.Empty()));

            adapter.Apply(new ChangeSet<ItemDescriptor>(ChangeSetType.Full, Selector.Make(1, "state1"), data, null,
                shouldPersist: changeSetShouldPersist));

            Assert.Equal(changeSetShouldPersist, _sink.Applies.ExpectValue().ShouldPersist);
        }
    }
}
