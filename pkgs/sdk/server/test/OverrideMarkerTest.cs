using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Server
{
    // The override marker is carried on the flag and segment models only. It is set on a copy
    // and it never appears in the JSON representation.
    public class OverrideMarkerTest
    {
        [Fact]
        public void FlagIsNotAnOverrideByDefault()
        {
            var flag = DataModelTest.MustParseFlag(DataModelTest.FlagWithAllPropertiesJson());
            Assert.False(flag.IsOverride);
        }

        [Fact]
        public void FlagAsOverrideMarksACopyAndKeepsEveryOtherProperty()
        {
            var json = DataModelTest.FlagWithAllPropertiesJson();
            var flag = DataModelTest.MustParseFlag(json);

            var marked = flag.AsOverride();

            Assert.True(marked.IsOverride);
            Assert.False(flag.IsOverride);
            Assert.NotSame(flag, marked);
            DataModelTest.AssertFlagHasAllProperties(marked);
            AssertJsonEqual(json, DataModel.Features.Serialize(new ItemDescriptor(marked.Version, marked)));
        }

        [Fact]
        public void FlagMarkerIsNeverSerialized()
        {
            var flag = new FeatureFlagBuilder("flag").Version(3).OnWithValue(LdValue.Of(true)).Build();
            var plainJson = DataModel.Features.Serialize(new ItemDescriptor(3, flag));
            var markedJson = DataModel.Features.Serialize(new ItemDescriptor(3, flag.AsOverride()));
            Assert.Equal(plainJson, markedJson);
            Assert.DoesNotContain("override", markedJson);
        }

        [Fact]
        public void AsOverrideOnAMarkedFlagReturnsTheSameInstance()
        {
            var marked = new FeatureFlagBuilder("flag").Build().AsOverride();
            Assert.Same(marked, marked.AsOverride());
        }

        [Fact]
        public void SegmentIsNotAnOverrideByDefault()
        {
            var segment = DataModelTest.MustParseSegment(DataModelTest.SegmentWithAllPropertiesJson());
            Assert.False(segment.IsOverride);
        }

        [Fact]
        public void SegmentAsOverrideMarksACopyAndKeepsEveryOtherProperty()
        {
            var json = DataModelTest.SegmentWithAllPropertiesJson();
            var segment = DataModelTest.MustParseSegment(json);

            var marked = segment.AsOverride();

            Assert.True(marked.IsOverride);
            Assert.False(segment.IsOverride);
            Assert.NotSame(segment, marked);
            DataModelTest.AssertSegmentHasAllProperties(marked);
            AssertJsonEqual(json, DataModel.Segments.Serialize(new ItemDescriptor(marked.Version, marked)));
            Assert.Equal(segment.Preprocessed.IncludedSet, marked.Preprocessed.IncludedSet);
            Assert.Equal(segment.Preprocessed.ExcludedSet, marked.Preprocessed.ExcludedSet);
        }

        [Fact]
        public void SegmentMarkerIsNeverSerialized()
        {
            var segment = new SegmentBuilder("segment").Version(3).Included("user").Build();
            var plainJson = DataModel.Segments.Serialize(new ItemDescriptor(3, segment));
            var markedJson = DataModel.Segments.Serialize(new ItemDescriptor(3, segment.AsOverride()));
            Assert.Equal(plainJson, markedJson);
            Assert.DoesNotContain("override", markedJson);
        }

        [Fact]
        public void AsOverrideOnAMarkedSegmentReturnsTheSameInstance()
        {
            var marked = new SegmentBuilder("segment").Build().AsOverride();
            Assert.Same(marked, marked.AsOverride());
        }
    }
}
