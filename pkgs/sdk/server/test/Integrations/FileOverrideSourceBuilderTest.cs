using System;
using System.Collections.Generic;
using System.IO;
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
            var prop = _tester.Property(b => b._parser, (b, v) => b.Parser(v));
            prop.AssertDefault(null);
            Func<string, object> p = s => s;
            prop.AssertCanSet(p);
        }

        [Fact]
        public void BuildRequiresAtLeastOneFilePath()
        {
            var e = Assert.Throws<ArgumentException>(() => FileOverrides.Source().Build(BasicContext));
            Assert.Contains("no file paths", e.Message);
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
