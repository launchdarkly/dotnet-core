using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    public class OverrideSinkTest
    {
        private readonly List<string> _notified = new List<string>();
        private bool _listening = true;

        private OverrideSink MakeSink(FakeReadOnlyStore baseStore, out OverrideLayer layer)
        {
            layer = new OverrideLayer();
            return new OverrideSink(layer, baseStore, keys => _notified.AddRange(keys), () => _listening,
                Logs.None.Logger(""));
        }

        private string[] TakeNotified()
        {
            var result = _notified.OrderBy(k => k).ToArray();
            _notified.Clear();
            return result;
        }

        [Fact]
        public void NotifiesOnAddChangeAndRemove()
        {
            var baseStore = new FakeReadOnlyStore().WithFlags(new FeatureFlagBuilder("flag1").Version(1).Build());
            var sink = MakeSink(baseStore, out var layer);

            // Adding an override is a change even though flag1 also exists in base data.
            sink.SetOverrides(OverrideLayerTest.FlagsOnly(
                new FeatureFlagBuilder("flag1").Version(1).Build(),
                new FeatureFlagBuilder("flag2").Version(1).Build()));
            Assert.Equal(new[] { "flag1", "flag2" }, TakeNotified());

            // An identical replacement, rebuilt from scratch, changes nothing.
            sink.SetOverrides(OverrideLayerTest.FlagsOnly(
                new FeatureFlagBuilder("flag1").Version(1).Build(),
                new FeatureFlagBuilder("flag2").Version(1).Build()));
            Assert.Empty(TakeNotified());

            // Changing one entry notifies only that entry.
            sink.SetOverrides(OverrideLayerTest.FlagsOnly(
                new FeatureFlagBuilder("flag1").Version(1).Build(),
                new FeatureFlagBuilder("flag2").Version(2).Build()));
            Assert.Equal(new[] { "flag2" }, TakeNotified());

            // A content change at the same version is still a change.
            sink.SetOverrides(OverrideLayerTest.FlagsOnly(
                new FeatureFlagBuilder("flag1").Version(1).On(true).Build(),
                new FeatureFlagBuilder("flag2").Version(2).Build()));
            Assert.Equal(new[] { "flag1" }, TakeNotified());

            // Removing overrides notifies them: flag1 reverts to base data, flag2 to not found.
            sink.SetOverrides(FullDataSet<ItemDescriptor>.Empty());
            Assert.Equal(new[] { "flag1", "flag2" }, TakeNotified());
            Assert.True(layer.IsEmpty);
        }

        [Fact]
        public void SegmentOverrideFansOutToDependentFlags()
        {
            var baseStore = new FakeReadOnlyStore()
                .WithFlags(
                    new FeatureFlagBuilder("dependent").Version(1).BooleanMatchingSegment("segment1").Build(),
                    new FeatureFlagBuilder("unrelated").Version(1).Build())
                .WithSegments(new SegmentBuilder("segment1").Version(1).Build());
            var sink = MakeSink(baseStore, out _);

            sink.SetOverrides(OverrideLayerTest.SegmentsOnly(new SegmentBuilder("segment1").Version(99).Build()));

            // The segment itself is not a flag, so only the dependent flag is notified.
            Assert.Equal(new[] { "dependent" }, TakeNotified());
        }

        [Fact]
        public void PrerequisiteFanOutUsesOldAndNewViews()
        {
            // The override for "parent" declares a prerequisite on "prereq". The base definition of
            // "parent" has no prerequisites. When the override is removed, the dependency edge only
            // exists in the old merged view. "parent" must still be notified when "prereq" changes in
            // the same replacement.
            var baseStore = new FakeReadOnlyStore().WithFlags(
                new FeatureFlagBuilder("parent").Version(1).Build(),
                new FeatureFlagBuilder("prereq").Version(1).Build());
            var sink = MakeSink(baseStore, out _);

            sink.SetOverrides(OverrideLayerTest.FlagsOnly(
                new FeatureFlagBuilder("parent").Version(1).Prerequisites(new Prerequisite("prereq", 0)).Build()));
            Assert.Equal(new[] { "parent" }, TakeNotified());

            // Replace the layer with an override of the prerequisite only. The "parent" override is
            // removed (a change) and "prereq" is added (a change). Fan-out through the old view's edge
            // also reaches "parent".
            sink.SetOverrides(OverrideLayerTest.FlagsOnly(new FeatureFlagBuilder("prereq").Version(99).Build()));
            Assert.Equal(new[] { "parent", "prereq" }, TakeNotified());

            // Now only "prereq" is overridden and nothing depends on it in the new view either.
            sink.SetOverrides(OverrideLayerTest.FlagsOnly(new FeatureFlagBuilder("prereq").Version(100).Build()));
            Assert.Equal(new[] { "prereq" }, TakeNotified());
        }

        [Fact]
        public void SkipsDiffWorkWithoutListeners()
        {
            var baseStore = new FakeReadOnlyStore { GetAllError = new InvalidOperationException("GetAll should not be called") };
            var sink = MakeSink(baseStore, out var layer);
            _listening = false;

            sink.SetOverrides(OverrideLayerTest.FlagsOnly(new FeatureFlagBuilder("flag1").Build()));

            Assert.NotNull(layer.Get(DataModel.Features, "flag1"));
            Assert.Empty(_notified);
            Assert.Equal(0, baseStore.GetAllCalls);
        }

        [Fact]
        public void ToleratesBaseReadFailure()
        {
            var baseStore = new FakeReadOnlyStore { GetAllError = new InvalidOperationException("sinkhole") };
            var sink = MakeSink(baseStore, out _);

            // Fan-out degrades, but the directly changed flags are still notified.
            sink.SetOverrides(OverrideLayerTest.FlagsOnly(new FeatureFlagBuilder("flag1").Build()));
            Assert.Equal(new[] { "flag1" }, TakeNotified());
        }

        [Fact]
        public void LayerIsReplacedBeforeListenersAreNotified()
        {
            var baseStore = new FakeReadOnlyStore();
            OverrideLayer layer = null;
            var seenInLayerAtNotify = new List<bool>();
            var sink = new OverrideSink(layer = new OverrideLayer(), baseStore,
                keys => seenInLayerAtNotify.Add(layer.Get(DataModel.Features, "flag1").HasValue),
                () => true, Logs.None.Logger(""));

            sink.SetOverrides(OverrideLayerTest.FlagsOnly(new FeatureFlagBuilder("flag1").Build()));

            Assert.Equal(new[] { true }, seenInLayerAtNotify);
        }
    }
}
