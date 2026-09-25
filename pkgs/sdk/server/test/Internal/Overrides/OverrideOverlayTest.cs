using System;
using System.Linq;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    public class OverrideOverlayTest
    {
        private static FeatureFlag RequireFlag(ItemDescriptor? item)
        {
            Assert.True(item.HasValue);
            return Assert.IsType<FeatureFlag>(item.Value.Item);
        }

        [Fact]
        public void GetPrefersTheOverrideEntry()
        {
            var baseStore = new FakeReadOnlyStore().WithFlags(
                new FeatureFlagBuilder("both").Version(1).Build(),
                new FeatureFlagBuilder("base-only").Version(1).Build());
            var layer = new OverrideLayer();
            layer.SetAll(OverrideLayerTest.FlagsOnly(
                new FeatureFlagBuilder("both").Version(99).Build(),
                new FeatureFlagBuilder("override-only").Version(1).Build()), out _, out _);
            var overlay = new OverrideOverlay(baseStore, layer);

            var both = overlay.Get(DataModel.Features, "both");
            Assert.Equal(99, both.Value.Version);
            Assert.True(RequireFlag(both).IsOverride);

            Assert.False(RequireFlag(overlay.Get(DataModel.Features, "base-only")).IsOverride);
            Assert.True(RequireFlag(overlay.Get(DataModel.Features, "override-only")).IsOverride);
            Assert.Null(overlay.Get(DataModel.Features, "nowhere"));
        }

        [Fact]
        public void GetServesOverridesFromAnUninitializedBase()
        {
            var baseStore = new FakeReadOnlyStore { IsInitialized = false };
            var layer = new OverrideLayer();
            layer.SetAll(OverrideLayerTest.FlagsOnly(new FeatureFlagBuilder("flag1").Build()), out _, out _);
            var overlay = new OverrideOverlay(baseStore, layer);

            Assert.True(RequireFlag(overlay.Get(DataModel.Features, "flag1")).IsOverride);
            Assert.False(overlay.Initialized());
        }

        [Fact]
        public void InitializedAndMetadataDelegateToTheBase()
        {
            var baseStore = new FakeReadOnlyStore { IsInitialized = true };
            var overlay = new OverrideOverlay(baseStore, new OverrideLayer());
            Assert.True(overlay.Initialized());
            baseStore.IsInitialized = false;
            Assert.False(overlay.Initialized());
            Assert.Null(overlay.GetMetadata());
        }

        [Fact]
        public void GetAllReturnsTheUnionWithOverridesWinning()
        {
            var baseStore = new FakeReadOnlyStore()
                .WithFlags(
                    new FeatureFlagBuilder("both").Version(1).Build(),
                    new FeatureFlagBuilder("base-only").Version(1).Build())
                .WithDeletedFlag("tombstone", 5);
            var layer = new OverrideLayer();
            layer.SetAll(OverrideLayerTest.FlagsOnly(
                new FeatureFlagBuilder("both").Version(99).Build(),
                new FeatureFlagBuilder("tombstone").Version(1).Build(),
                new FeatureFlagBuilder("override-only").Version(1).Build()), out _, out _);
            var overlay = new OverrideOverlay(baseStore, layer);

            var byKey = overlay.GetAll(DataModel.Features).Items.ToDictionary(kv => kv.Key, kv => kv.Value);

            Assert.Equal(4, byKey.Count);
            Assert.Equal(99, byKey["both"].Version);
            Assert.True(RequireFlag(byKey["both"]).IsOverride);
            Assert.False(RequireFlag(byKey["base-only"]).IsOverride);
            Assert.NotNull(byKey["tombstone"].Item);
            Assert.True(RequireFlag(byKey["override-only"]).IsOverride);
        }

        [Fact]
        public void GetAllWithAnEmptyLayerIsAPassthrough()
        {
            var baseStore = new FakeReadOnlyStore().WithFlags(new FeatureFlagBuilder("flag1").Build());
            var overlay = new OverrideOverlay(baseStore, new OverrideLayer());

            Assert.Single(overlay.GetAll(DataModel.Features).Items);

            baseStore.GetAllError = new InvalidOperationException("sinkhole");
            Assert.Throws<InvalidOperationException>(() => overlay.GetAll(DataModel.Features));
        }

        [Fact]
        public void GetAllServesOverridesWhenTheBaseFails()
        {
            var baseStore = new FakeReadOnlyStore { GetAllError = new InvalidOperationException("sinkhole") };
            var layer = new OverrideLayer();
            layer.SetAll(OverrideLayerTest.FlagsOnly(
                new FeatureFlagBuilder("override-1").Version(1).Build(),
                new FeatureFlagBuilder("override-2").Version(2).Build()), out _, out _);
            var overlay = new OverrideOverlay(baseStore, layer);

            var items = overlay.GetAll(DataModel.Features).Items.ToList();

            Assert.Equal(2, items.Count);
            Assert.All(items, kv => Assert.True(RequireFlag(kv.Value).IsOverride));
            Assert.Contains(items, kv => kv.Key == "override-1");
            Assert.Contains(items, kv => kv.Key == "override-2");
        }

        [Fact]
        public void GetAllOfSegmentsAppliesTheSamePrecedence()
        {
            var baseStore = new FakeReadOnlyStore().WithSegments(new SegmentBuilder("s1").Version(1).Build());
            var layer = new OverrideLayer();
            layer.SetAll(OverrideLayerTest.SegmentsOnly(new SegmentBuilder("s1").Version(9).Build()), out _, out _);
            var overlay = new OverrideOverlay(baseStore, layer);

            var items = overlay.GetAll(DataModel.Segments).Items.ToList();
            Assert.Single(items);
            Assert.Equal(9, items[0].Value.Version);
            Assert.True(Assert.IsType<Segment>(items[0].Value.Item).IsOverride);
        }
    }
}
