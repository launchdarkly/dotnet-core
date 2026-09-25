using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    public class OverrideLayerTest
    {
        internal static FullDataSet<ItemDescriptor> DataSetOf(IEnumerable<FeatureFlag> flags, IEnumerable<Segment> segments)
        {
            var builder = new DataSetBuilder();
            foreach (var flag in flags)
            {
                builder.Flags(flag);
            }
            foreach (var segment in segments)
            {
                builder.Segments(segment);
            }
            return builder.Build();
        }

        internal static FullDataSet<ItemDescriptor> FlagsOnly(params FeatureFlag[] flags) =>
            DataSetOf(flags, new Segment[0]);

        internal static FullDataSet<ItemDescriptor> SegmentsOnly(params Segment[] segments) =>
            DataSetOf(new FeatureFlag[0], segments);

        private static FeatureFlag RequireFlag(ItemDescriptor? item)
        {
            Assert.True(item.HasValue);
            return Assert.IsType<FeatureFlag>(item.Value.Item);
        }

        [Fact]
        public void LayerStartsEmpty()
        {
            var layer = new OverrideLayer();
            Assert.True(layer.IsEmpty);
            Assert.Null(layer.Get(DataModel.Features, "flag1"));
            Assert.Empty(layer.All(DataModel.Features));
            Assert.Empty(layer.All(DataModel.Segments));
        }

        [Fact]
        public void SetAllMarksCopiesWithoutMutatingTheSource()
        {
            var layer = new OverrideLayer();
            var flag = new FeatureFlagBuilder("flag1").Version(2).Build();
            var segment = new SegmentBuilder("segment1").Version(3).Build();

            layer.SetAll(DataSetOf(new[] { flag }, new[] { segment }), out _, out _);

            Assert.False(flag.IsOverride);
            Assert.False(segment.IsOverride);

            var storedFlag = layer.Get(DataModel.Features, "flag1");
            Assert.True(RequireFlag(storedFlag).IsOverride);
            Assert.Equal(2, storedFlag.Value.Version);

            var storedSegment = layer.Get(DataModel.Segments, "segment1");
            Assert.True(storedSegment.HasValue);
            Assert.True(Assert.IsType<Segment>(storedSegment.Value.Item).IsOverride);
            Assert.Equal(3, storedSegment.Value.Version);
        }

        [Fact]
        public void MarkedCopySharesImmutablePartsAndKeepsContent()
        {
            var flag = new FeatureFlagBuilder("flag1").Version(2).On(true).FallthroughVariation(1)
                .Variations("a", "b")
                .Rules(new RuleBuilder().Id("r").Variation(1).Clauses(ClauseBuilder.ShouldMatchUser(Context.New("u"))).Build())
                .Build();

            var marked = RequireFlag(OverrideLayer.MarkedCopy(new ItemDescriptor(2, flag)));

            Assert.True(marked.IsOverride);
            Assert.Same(flag.Rules, marked.Rules);
            Assert.Same(flag.Prerequisites, marked.Prerequisites);
            Assert.Equal(flag.ToJsonString(), marked.ToJsonString());
        }

        [Fact]
        public void MarkedCopyLeavesDeletedItemsAndOtherTypesAlone()
        {
            var deleted = ItemDescriptor.Deleted(5);
            var copy = OverrideLayer.MarkedCopy(deleted);
            Assert.Null(copy.Item);
            Assert.Equal(5, copy.Version);

            var other = new ItemDescriptor(1, "not a model object");
            Assert.Same(other.Item, OverrideLayer.MarkedCopy(other).Item);
        }

        [Fact]
        public void ReplacementSemantics()
        {
            var layer = new OverrideLayer();

            layer.SetAll(FlagsOnly(new FeatureFlagBuilder("flag1").Build()), out var previous, out var current);
            Assert.False(layer.IsEmpty);
            Assert.NotNull(layer.Get(DataModel.Features, "flag1"));
            Assert.Empty(previous);
            Assert.Single(current[DataModel.Features]);

            // A replacement is a full snapshot: entries absent from it are removed.
            layer.SetAll(FlagsOnly(new FeatureFlagBuilder("flag2").Build()), out previous, out current);
            Assert.Null(layer.Get(DataModel.Features, "flag1"));
            Assert.NotNull(layer.Get(DataModel.Features, "flag2"));
            Assert.True(previous[DataModel.Features].ContainsKey("flag1"));
            Assert.True(current[DataModel.Features].ContainsKey("flag2"));

            layer.SetAll(FullDataSet<ItemDescriptor>.Empty(), out _, out _);
            Assert.True(layer.IsEmpty);
            Assert.Null(layer.Get(DataModel.Features, "flag2"));
        }

        [Fact]
        public void EmptyCollectionsCountAsEmpty()
        {
            var layer = new OverrideLayer();
            layer.SetAll(DataSetBuilder.Empty, out _, out _);
            Assert.True(layer.IsEmpty);
        }

        [Fact]
        public void AllReturnsTheSnapshotOfOneKind()
        {
            var layer = new OverrideLayer();
            layer.SetAll(DataSetOf(
                new[] { new FeatureFlagBuilder("flag1").Build(), new FeatureFlagBuilder("flag2").Build() },
                new[] { new SegmentBuilder("segment1").Build() }), out _, out _);

            var flags = layer.All(DataModel.Features);
            Assert.Equal(2, flags.Count);
            Assert.True(RequireFlag(flags["flag1"]).IsOverride);
            Assert.Single(layer.All(DataModel.Segments));

            // The snapshot is immutable: a later replacement does not change it.
            layer.SetAll(FullDataSet<ItemDescriptor>.Empty(), out _, out _);
            Assert.Equal(2, flags.Count);
        }
    }
}
