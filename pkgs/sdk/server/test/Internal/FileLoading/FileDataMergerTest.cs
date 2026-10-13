using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataMergerTest
    {
        private static FeatureFlag Expand(string key, LdValue value) =>
            FileDataParser.MakeFallthroughFlagWithValue(key, value, 0);

        private static FileDataDocument DocWithFlag(FeatureFlag flag) =>
            new FileDataDocument(new[] { new KeyValuePair<string, FeatureFlag>(flag.Key, flag) }, null, null);

        private static FileDataDocument DocWithFlagValue(string key, LdValue value) =>
            new FileDataDocument(null, new[] { new KeyValuePair<string, LdValue>(key, value) }, null);

        private static FileDataDocument DocWithSegment(Segment segment) =>
            new FileDataDocument(null, null, new[] { new KeyValuePair<string, Segment>(segment.Key, segment) });

        private static FileDataMergeResult Merge(FileDataDuplicateKeysHandling handling, params FileDataDocument[] docs) =>
            FileDataMerger.Merge(handling, docs, Expand);

        [Fact]
        public void CombinesDocuments()
        {
            var flag1 = new FeatureFlagBuilder("flag1").Version(2).Build();
            var segment1 = new SegmentBuilder("segment1").Version(4).Build();

            var result = Merge(FileDataDuplicateKeysHandling.Fail,
                DocWithFlag(flag1), DocWithFlagValue("flag2", LdValue.Of(true)), DocWithSegment(segment1));

            Assert.Equal(2, result.Flags.Count);
            Assert.Equal("flag1", result.Flags[0].Key);
            Assert.Equal(2, result.Flags[0].Value.Version);
            Assert.Same(flag1, result.Flags[0].Value.Item);
            Assert.Equal("flag2", result.Flags[1].Key);
            var expanded = Assert.IsType<FeatureFlag>(result.Flags[1].Value.Item);
            Assert.Equal(new[] { LdValue.Of(true) }, expanded.Variations);

            Assert.Single(result.Segments);
            Assert.Equal("segment1", result.Segments[0].Key);
            Assert.Equal(4, result.Segments[0].Value.Version);
            Assert.Same(segment1, result.Segments[0].Value.Item);
        }

        [Fact]
        public void DuplicateFlagFails()
        {
            var flagA = new FeatureFlagBuilder("flag1").Version(1).Build();
            var flagB = new FeatureFlagBuilder("flag1").Version(2).Build();
            var e = Assert.Throws<FileDataException>(() =>
                Merge(FileDataDuplicateKeysHandling.Fail, DocWithFlag(flagA), DocWithFlag(flagB)));
            Assert.Equal("flag \"flag1\" is specified by multiple files", e.Message);
        }

        [Fact]
        public void UnrecognizedHandlingBehavesAsFail()
        {
            var flagA = new FeatureFlagBuilder("flag1").Version(1).Build();
            var flagB = new FeatureFlagBuilder("flag1").Version(2).Build();
            Assert.Throws<FileDataException>(() =>
                Merge((FileDataDuplicateKeysHandling)99, DocWithFlag(flagA), DocWithFlag(flagB)));
        }

        [Fact]
        public void IgnoreKeepsFirstOccurrence()
        {
            var flagA = new FeatureFlagBuilder("flag1").Version(1).Build();
            var flagB = new FeatureFlagBuilder("flag1").Version(2).Build();
            var result = Merge(FileDataDuplicateKeysHandling.Ignore, DocWithFlag(flagA), DocWithFlag(flagB));
            Assert.Single(result.Flags);
            Assert.Equal(1, result.Flags[0].Value.Version);
            Assert.Same(flagA, result.Flags[0].Value.Item);
        }

        [Fact]
        public void FullFlagAndFlagValueCollide()
        {
            var flag = new FeatureFlagBuilder("flag1").Version(1).Build();
            Assert.Throws<FileDataException>(() =>
                Merge(FileDataDuplicateKeysHandling.Fail, DocWithFlag(flag), DocWithFlagValue("flag1", LdValue.Of(true))));
        }

        [Fact]
        public void FlagValueAndFullFlagCollideWithinOneDocument()
        {
            var flag = new FeatureFlagBuilder("flag1").Version(1).Build();
            var doc = new FileDataDocument(
                new[] { new KeyValuePair<string, FeatureFlag>("flag1", flag) },
                new[] { new KeyValuePair<string, LdValue>("flag1", LdValue.Of(true)) },
                null);
            Assert.Throws<FileDataException>(() => Merge(FileDataDuplicateKeysHandling.Fail, doc));
        }

        [Fact]
        public void DuplicateSegmentFails()
        {
            var segment = new SegmentBuilder("segment1").Build();
            var e = Assert.Throws<FileDataException>(() =>
                Merge(FileDataDuplicateKeysHandling.Fail, DocWithSegment(segment), DocWithSegment(segment)));
            Assert.Equal("segment \"segment1\" is specified by multiple files", e.Message);
        }

        [Fact]
        public void FlagAndSegmentWithTheSameKeyDoNotCollide()
        {
            var flag = new FeatureFlagBuilder("same").Build();
            var segment = new SegmentBuilder("same").Build();
            var result = Merge(FileDataDuplicateKeysHandling.Fail, DocWithFlag(flag), DocWithSegment(segment));
            Assert.Single(result.Flags);
            Assert.Single(result.Segments);
        }

        [Fact]
        public void PreservesDocumentOrder()
        {
            var expectedKeys = new[] { "flag-a", "flag-b", "flag-c", "flag-d", "flag-e" };
            var docs = expectedKeys.Select(key => DocWithFlag(new FeatureFlagBuilder(key).Build())).ToArray();
            var result = Merge(FileDataDuplicateKeysHandling.Fail, docs);
            Assert.Equal(expectedKeys, result.Flags.Select(kv => kv.Key));
        }

        [Fact]
        public void CountsEntriesKeptFromEachDocument()
        {
            var first = new FileDataDocument(null, new[]
            {
                new KeyValuePair<string, LdValue>("a", LdValue.Of(true)),
                new KeyValuePair<string, LdValue>("shared", LdValue.Of(true))
            }, null);
            var second = new FileDataDocument(null, new[]
            {
                new KeyValuePair<string, LdValue>("b", LdValue.Of(true)),
                new KeyValuePair<string, LdValue>("shared", LdValue.Of(false))
            }, new[] { new KeyValuePair<string, Segment>("seg", new SegmentBuilder("seg").Build()) });

            var result = Merge(FileDataDuplicateKeysHandling.Ignore, first, second);

            Assert.Equal(2, result.Documents.Count);
            Assert.Equal(2, result.Documents[0].Flags);
            Assert.Equal(0, result.Documents[0].Segments);
            // The duplicate "shared" entry from the second document is dropped and not counted.
            Assert.Equal(1, result.Documents[1].Flags);
            Assert.Equal(1, result.Documents[1].Segments);
            Assert.Empty(result.Files);
        }

        [Fact]
        public void NoDocumentsProduceAnEmptyResult()
        {
            var result = Merge(FileDataDuplicateKeysHandling.Fail);
            Assert.Empty(result.Flags);
            Assert.Empty(result.Segments);
            Assert.Empty(result.Documents);
        }
    }
}
