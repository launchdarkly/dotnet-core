using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Internal.Overrides;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class FileOverrideSourceBuilderTest : BaseTest
    {
        private readonly BuilderBehavior.InternalStateTester<FileOverrideSourceBuilder> _tester =
            BuilderBehavior.For(FileOverrides.Source);

        public FileOverrideSourceBuilderTest(ITestOutputHelper testOutput) : base(testOutput) { }

        private FileOverrideSource BuildSource(FileOverrideSourceBuilder builder) =>
            Assert.IsType<FileOverrideSource>(builder.Build(BasicContext));

        [Fact]
        public void FilePaths()
        {
            Assert.Empty(FileOverrides.Source()._paths);

            var b = FileOverrides.Source();
            b.FilePaths("path1");
            b.FilePaths("path2", "path3");
            Assert.Equal(new List<string> { "path1", "path2", "path3" }, b._paths);
        }

        [Fact]
        public void DuplicateKeysHandling()
        {
            var prop = _tester.Property(b => b._duplicateKeysHandling, (b, v) => b.DuplicateKeysHandling(v));
            prop.AssertDefault(FileOverrideTypes.DuplicateKeysHandling.Fail);
            prop.AssertCanSet(FileOverrideTypes.DuplicateKeysHandling.Ignore);
        }

        [Fact]
        public void ChangeDetection()
        {
            var prop = _tester.Property(b => b._changeDetection, (b, v) => b.ChangeDetection(v));
            prop.AssertDefault(FileOverrideTypes.ChangeDetection.Polling);
            prop.AssertCanSet(FileOverrideTypes.ChangeDetection.Watching);
        }

        [Fact]
        public void PollInterval()
        {
            var prop = _tester.Property(b => b._pollInterval, (b, v) => b.PollInterval(v));
            prop.AssertDefault(FileOverrideSourceBuilder.DefaultPollInterval);
            prop.AssertCanSet(TimeSpan.FromSeconds(5));
            Assert.Equal(TimeSpan.FromSeconds(1), FileOverrideSourceBuilder.DefaultPollInterval);
            Assert.Equal(TimeSpan.FromSeconds(1), FileOverrideSourceBuilder.MinimumPollInterval);
        }

        [Fact]
        public void Parser()
        {
            Assert.Empty(FileOverrides.Source()._parsers);

            Func<string, object> p = s => s;
            Func<string, object> q = s => null;
            var b = FileOverrides.Source().Parser("YAML", p).Parser(".yml", p).Parser(".yaml", q);
            Assert.Equal(new[] { ".yaml", ".yml" }, b._parsers.Keys.OrderBy(k => k));
            Assert.Same(q, b._parsers[".yaml"]);
            Assert.Same(p, b._parsers[".yml"]);

            Assert.Throws<ArgumentException>(() => FileOverrides.Source().Parser("", p));
            Assert.Throws<ArgumentException>(() => FileOverrides.Source().Parser(null, p));
            Assert.Throws<ArgumentNullException>(() => FileOverrides.Source().Parser(".yaml", null));
        }

        [Fact]
        public void BuildRequiresAtLeastOneFilePath()
        {
            var e = Assert.Throws<ArgumentException>(() => FileOverrides.Source().Build(BasicContext));
            Assert.Contains("no file paths", e.Message);
        }

        [Fact]
        public void BuildRejectsAYamlFileWithoutARegisteredParser()
        {
            var e = Assert.Throws<ArgumentException>(() =>
                FileOverrides.Source().FilePaths("/a.json", "/b.yml").Build(BasicContext));
            Assert.Contains(Path.GetFullPath("/b.yml"), e.Message);
            Assert.Contains("Parser(\".yml\", ...)", e.Message);

            e = Assert.Throws<ArgumentException>(() =>
                FileOverrides.Source().FilePaths("/c.YAML").Parser(".yml", s => null).Build(BasicContext));
            Assert.Contains(Path.GetFullPath("/c.YAML"), e.Message);
        }

        [Fact]
        public void BuildAcceptsAYamlFileWithARegisteredParser()
        {
            using (BuildSource(FileOverrides.Source().FilePaths("/b.yml", "/c.YAML")
                .Parser("yml", s => null).Parser(".yaml", s => null))) { }
        }

        [Fact]
        public void BuildAcceptsFilesWithOtherExtensionsWithoutAParser()
        {
            using (BuildSource(FileOverrides.Source().FilePaths("/a.json", "/b", "/c.txt"))) { }
        }

        [Fact]
        public void BuildPassesOptionsToTheSource()
        {
            var source = BuildSource(FileOverrides.Source().FilePaths("/a.json", "/b.json")
                .DuplicateKeysHandling(FileOverrideTypes.DuplicateKeysHandling.Ignore)
                .ChangeDetection(FileOverrideTypes.ChangeDetection.Watching)
                .PollInterval(TimeSpan.FromSeconds(3)));

            Assert.Equal(new[] { Path.GetFullPath("/a.json"), Path.GetFullPath("/b.json") }, source.Paths);
            Assert.Equal(FileOverrideTypes.DuplicateKeysHandling.Ignore, source.DuplicateKeysHandling);
            Assert.Equal(FileOverrideTypes.ChangeDetection.Watching, source.ChangeDetection);
            Assert.Equal(TimeSpan.FromSeconds(3), source.PollInterval);
        }

        [Fact]
        public void BuildResolvesRelativePathsAgainstTheCurrentDirectory()
        {
            var source = BuildSource(FileOverrides.Source().FilePaths("relative/overrides.json"));
            Assert.Equal(new[] { Path.GetFullPath("relative/overrides.json") }, source.Paths);
            Assert.True(Path.IsPathRooted(source.Paths[0]));
        }

        [Fact]
        public void BuildDropsARepeatedPathAndWarns()
        {
            // Under the default fail handling the same file listed twice would make every one of its
            // keys a duplicate of itself. The repeat is ignored and the first occurrence keeps its place.
            var source = BuildSource(FileOverrides.Source().FilePaths("/a.json", "/b.json", "/a.json"));

            Assert.Equal(new[] { Path.GetFullPath("/a.json"), Path.GetFullPath("/b.json") }, source.Paths);
            AssertLogMessageRegex(true, LogLevel.Warn, "more than once");
        }

        [Fact]
        public void BuildDefaultsToPollingEverySecond()
        {
            var source = BuildSource(FileOverrides.Source().FilePaths("/a.json"));
            Assert.Equal(FileOverrideTypes.ChangeDetection.Polling, source.ChangeDetection);
            Assert.Equal(FileOverrideSourceBuilder.DefaultPollInterval, source.PollInterval);
        }

        [Fact]
        public void BuildRaisesAPollIntervalBelowTheMinimumAndWarns()
        {
            var source = BuildSource(FileOverrides.Source().FilePaths("/a.json").PollInterval(TimeSpan.FromMilliseconds(1)));
            Assert.Equal(FileOverrideSourceBuilder.MinimumPollInterval, source.PollInterval);
            AssertLogMessageRegex(true, LogLevel.Warn, "below the minimum");
        }

        [Fact]
        public void BuildKeepsAPollIntervalAtOrAboveTheMinimum()
        {
            var source = BuildSource(FileOverrides.Source().FilePaths("/a.json").PollInterval(TimeSpan.FromSeconds(2)));
            Assert.Equal(TimeSpan.FromSeconds(2), source.PollInterval);
            AssertLogMessageRegex(false, LogLevel.Warn, "below the minimum");
        }

        [Fact]
        public void BuildRejectsAPollIntervalAboveTheMaximum()
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => FileOverrides.Source().FilePaths("/a.json")
                .PollInterval(FileOverrideSourceBuilder.MaximumPollInterval + TimeSpan.FromMilliseconds(1)).Build(BasicContext));
            Assert.Contains("must not exceed", e.Message);
        }

        [Fact]
        public void BuildDoesNotWarnAboutThePollIntervalInWatchingMode()
        {
            var source = BuildSource(FileOverrides.Source().FilePaths("/a.json")
                .ChangeDetection(FileOverrideTypes.ChangeDetection.Watching).PollInterval(TimeSpan.FromMilliseconds(1)));
            Assert.Equal(FileOverrideTypes.ChangeDetection.Watching, source.ChangeDetection);
            AssertLogMessageRegex(false, LogLevel.Warn, "below the minimum");
        }

        [Fact]
        public void BuildRejectsAnUnknownDuplicateKeysHandling()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FileOverrides.Source().FilePaths("/a.json")
                .DuplicateKeysHandling((FileOverrideTypes.DuplicateKeysHandling)99).Build(BasicContext));
        }

        [Fact]
        public void BuildRejectsAnUnknownChangeDetection()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FileOverrides.Source().FilePaths("/a.json")
                .ChangeDetection((FileOverrideTypes.ChangeDetection)99).Build(BasicContext));
        }

        [Fact]
        public void BuildDoesNotRequireTheFilesToExist()
        {
            var source = BuildSource(FileOverrides.Source().FilePaths(Path.Combine(Path.GetTempPath(), "no-such-overrides.json")));
            Assert.NotNull(source);
        }
    }
}
