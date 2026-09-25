using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Internal.Evaluation;
using LaunchDarkly.Sdk.Server.Internal.FileLoading;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using YamlDotNet.Serialization;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    public class FileOverrideSourceTest : BaseTest, IDisposable
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ShortPollInterval = TimeSpan.FromMilliseconds(50);

        private readonly TempDirectory _dir = TempDirectory.Create();
        private readonly EventSink<FullDataSet<ItemDescriptor>> _snapshots = new EventSink<FullDataSet<ItemDescriptor>>();
        private readonly CapturingSink _sink;
        private FileOverrideSource _source;

        public FileOverrideSourceTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _sink = new CapturingSink(_snapshots);
        }

        public void Dispose()
        {
            _source?.Dispose();
            _dir.Dispose();
        }

        private class CapturingSink : IOverrideSink
        {
            private readonly EventSink<FullDataSet<ItemDescriptor>> _snapshots;
            public CapturingSink(EventSink<FullDataSet<ItemDescriptor>> snapshots) { _snapshots = snapshots; }
            public void SetOverrides(FullDataSet<ItemDescriptor> data) => _snapshots.Enqueue(data);
        }

        private FileOverrideSource StartSource(
            IEnumerable<string> paths,
            FileOverrideTypes.DuplicateKeysHandling duplicateKeysHandling = FileOverrideTypes.DuplicateKeysHandling.Fail,
            FileOverrideTypes.ChangeDetection changeDetection = FileOverrideTypes.ChangeDetection.Polling,
            TimeSpan? pollInterval = null,
            Func<string, object> parser = null
            )
        {
            _source = new FileOverrideSource(paths.ToList(), duplicateKeysHandling, changeDetection,
                pollInterval ?? ShortPollInterval, parser, TestLogger);
            _source.Start(_sink);
            return _source;
        }

        private static void Write(string path, string content) => File.WriteAllText(path, content);

        private FullDataSet<ItemDescriptor> RequireSnapshot() => _snapshots.ExpectValue(TestTimeout);

        // The initial load happens inside Start, so its snapshot is already queued.
        private FullDataSet<ItemDescriptor> RequireInitialSnapshot() => _snapshots.ExpectValue(TimeSpan.Zero);

        private void RequireNoSnapshot(TimeSpan duration) => _snapshots.ExpectNoValue(duration);

        private static Dictionary<string, FeatureFlag> FlagsByKey(FullDataSet<ItemDescriptor> snapshot) =>
            snapshot.Data.Where(kv => kv.Key == DataModel.Features).SelectMany(kv => kv.Value.Items)
                .ToDictionary(kv => kv.Key, kv => Assert.IsType<FeatureFlag>(kv.Value.Item));

        private static Dictionary<string, Segment> SegmentsByKey(FullDataSet<ItemDescriptor> snapshot) =>
            snapshot.Data.Where(kv => kv.Key == DataModel.Segments).SelectMany(kv => kv.Value.Items)
                .ToDictionary(kv => kv.Key, kv => Assert.IsType<Segment>(kv.Value.Item));

        // Waits for an Info log line that contains every substring. The source logs after it hands the
        // snapshot to the sink, so a test that has just received a snapshot may run ahead of the line.
        private void RequireInfoLine(params string[] substrings)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (LogCapture.GetMessages().Any(m => m.Level == LogLevel.Info && substrings.All(s => m.Text.Contains(s))))
                {
                    return;
                }
                Thread.Sleep(10);
            }
            Assert.True(false, "timed out waiting for an Info line containing " + string.Join(" and ", substrings)
                + "\nlog output:\n" + LogCapture);
        }

        [Fact]
        public void LoadsInitialDataSynchronously()
        {
            var path = _dir.PathOf("overrides.json");
            Write(path, @"{""flagValues"": {""flag1"": true}, ""flags"": {""flag2"": {""key"": ""flag2"", ""version"": 3, ""on"": false}},
                ""segments"": {""seg1"": {""key"": ""seg1"", ""version"": 4}}}");

            StartSource(new[] { path });

            var snapshot = RequireInitialSnapshot();
            var flags = FlagsByKey(snapshot);
            Assert.Equal(2, flags.Count);
            Assert.Equal(3, flags["flag2"].Version);
            Assert.Equal(4, SegmentsByKey(snapshot)["seg1"].Version);

            // The flag-value entry was expanded into a full flag definition that is off and serves its
            // single value for every context.
            var expanded = flags["flag1"];
            Assert.False(expanded.IsOverride, "the source supplies plain definitions; the SDK marks them");
            Assert.Equal(new[] { LdValue.Of(true) }, expanded.Variations);
            Assert.False(expanded.On);
            Assert.Equal(0, expanded.OffVariation);
            var result = EvaluatorTestUtil.BasicEvaluator.Evaluate(expanded, Context.New("anyone"));
            Assert.Equal(LdValue.Of(true), result.Result.Value);
            Assert.Equal(0, result.Result.VariationIndex);
            Assert.Equal(EvaluationReasonKind.Off, result.Result.Reason.Kind);
        }

        [Fact]
        public void LoadsYamlWithAParser()
        {
            var path = _dir.PathOf("overrides.yaml");
            Write(path, "flagValues:\n  flag1: true\n");
            var yaml = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();

            StartSource(new[] { path }, parser: s => yaml.Deserialize<object>(s));

            var flags = FlagsByKey(RequireInitialSnapshot());
            Assert.Single(flags);
            Assert.Equal(new[] { LdValue.Of(true) }, flags["flag1"].Variations);
        }

        [Fact]
        public void YamlWithoutAParserFailsTheLoad()
        {
            var path = _dir.PathOf("overrides.yaml");
            Write(path, "flagValues:\n  flag1: true\n");

            StartSource(new[] { path });

            RequireNoSnapshot(TimeSpan.FromMilliseconds(200));
            AssertLogMessageRegex(true, LogLevel.Error, "Unable to load flags: error parsing file");
        }

        [Fact]
        public void MergesFilesInConfiguredOrder()
        {
            var first = _dir.PathOf("first.json");
            var second = _dir.PathOf("second.json");
            Write(first, @"{""flags"": {""flag1"": {""key"": ""flag1"", ""version"": 1}}}");
            Write(second, @"{""flags"": {""flag1"": {""key"": ""flag1"", ""version"": 2}}}");

            StartSource(new[] { first, second }, FileOverrideTypes.DuplicateKeysHandling.Ignore);

            var flags = FlagsByKey(RequireInitialSnapshot());
            Assert.Single(flags);
            Assert.Equal(1, flags["flag1"].Version);
        }

        [Fact]
        public void DuplicateKeysFailByDefault()
        {
            var first = _dir.PathOf("first.json");
            var second = _dir.PathOf("second.json");
            Write(first, @"{""flags"": {""flag1"": {""key"": ""flag1"", ""version"": 1}}}");
            Write(second, @"{""flags"": {""flag1"": {""key"": ""flag1"", ""version"": 2}}}");

            StartSource(new[] { first, second });

            RequireNoSnapshot(TimeSpan.FromMilliseconds(200));
            AssertLogMessageRegex(true, LogLevel.Error, "is specified by multiple files");
        }

        [Fact]
        public void StartsWithAMissingFileAndPicksItUpWhenItAppears()
        {
            var path = _dir.PathOf("not-yet.json");

            StartSource(new[] { path });

            // A missing file contributes no overrides. The initial snapshot is empty.
            Assert.Empty(FlagsByKey(RequireInitialSnapshot()));
            AssertLogMessageRegex(false, LogLevel.Error, ".*");
            AssertLogMessageRegex(false, LogLevel.Warn, ".*");

            // Once the file appears, the change signal picks it up unprompted.
            Write(path, @"{""flagValues"": {""flag1"": true}}");
            Assert.Single(FlagsByKey(RequireSnapshot()));
        }

        [Fact]
        public void MissingFileContributesNoEntries()
        {
            var first = _dir.PathOf("first.json");
            var second = _dir.PathOf("second.json");
            Write(first, @"{""flagValues"": {""from-first"": true}}");

            // Step 1: one configured file exists and one does not. The existing file applies.
            StartSource(new[] { first, second });
            var flags = FlagsByKey(RequireInitialSnapshot());
            Assert.Equal(new[] { "from-first" }, flags.Keys);

            // Step 2: the second file appears. Both apply.
            Write(second, @"{""flagValues"": {""from-second"": true}}");
            flags = FlagsByKey(RequireSnapshot());
            Assert.Equal(2, flags.Count);

            // Step 3: the second file is deleted. Its overrides are removed.
            File.Delete(second);
            flags = FlagsByKey(RequireSnapshot());
            Assert.Equal(new[] { "from-first" }, flags.Keys);

            // Step 4: the last file is deleted. The layer is cleared.
            File.Delete(first);
            Assert.Empty(FlagsByKey(RequireSnapshot()));
        }

        [Fact]
        public void LogsOverridesInEffectOnEachChange()
        {
            var first = _dir.PathOf("first.json");
            var second = _dir.PathOf("second.json");
            Write(first, @"{""flagValues"": {""flag1"": true, ""flag2"": false}, ""segments"": {""seg"": {""key"": ""seg"", ""version"": 1}}}");

            // Step 1: at startup, one file supplies entries and the other is absent.
            StartSource(new[] { first, second });
            RequireInitialSnapshot();
            RequireInfoLine("Flag overrides in effect: 2 flags, 1 segment", first + ": 2 flags, 1 segment", second + ": absent");

            // Step 2: the absent file appears with one entry.
            Write(second, @"{""flagValues"": {""flag3"": true}}");
            RequireSnapshot();
            RequireInfoLine("Flag overrides in effect: 3 flags, 1 segment", second + ": 1 flag");

            // Step 3: both files are deleted. Nothing is in effect.
            File.Delete(first);
            File.Delete(second);
            RequireInfoLine("Flag overrides: none in effect", first + ": absent", second + ": absent");
        }

        [Fact]
        public void LogsNoneInEffectAtStartupWithoutFiles()
        {
            var path = _dir.PathOf("overrides.json");
            StartSource(new[] { path });
            RequireInitialSnapshot();
            RequireInfoLine("Flag overrides: none in effect", path + ": absent");
        }

        [Fact]
        public void LogsAFileWithNoEntries()
        {
            var path = _dir.PathOf("overrides.json");
            Write(path, "{}");
            StartSource(new[] { path });
            RequireInitialSnapshot();
            RequireInfoLine("Flag overrides: none in effect", path + ": no entries");
        }

        [Fact]
        public void CountsTextPluralizes()
        {
            Assert.Equal("1 flag", FileOverrideSource.CountsText(1, 0));
            Assert.Equal("2 flags, 1 segment", FileOverrideSource.CountsText(2, 1));
            Assert.Equal("3 segments", FileOverrideSource.CountsText(0, 3));
            Assert.Equal("", FileOverrideSource.CountsText(0, 0));
        }

        [Fact]
        public void WatchingModeIsQuietWhenTheFileIsAbsent()
        {
            var path = _dir.PathOf("overrides.json");

            StartSource(new[] { path }, changeDetection: FileOverrideTypes.ChangeDetection.Watching);
            RequireInitialSnapshot();

            Thread.Sleep(300);
            Assert.DoesNotContain(LogCapture.GetMessages(), m => m.Level == LogLevel.Error || m.Level == LogLevel.Warn);

            Write(path, @"{""flagValues"": {""flag1"": true}}");
            Assert.Single(FlagsByKey(RequireSnapshot()));
        }

        [Fact]
        public void WatchingModeReloadsOnChange()
        {
            var path = _dir.PathOf("overrides.json");
            Write(path, @"{""flagValues"": {""flag1"": true}}");

            var source = StartSource(new[] { path }, changeDetection: FileOverrideTypes.ChangeDetection.Watching);
            Assert.IsType<FileDataWatcher>(source.ChangeDetector);
            RequireInitialSnapshot();

            Write(path, @"{""flagValues"": {""flag1"": true, ""flag2"": false}}");
            Assert.Equal(2, FlagsByKey(RequireSnapshot()).Count);

            // Removing entries removes them from the snapshot. A reload is a full replacement.
            Write(path, "{}");
            Assert.Empty(FlagsByKey(RequireSnapshot()));
        }

        [Fact]
        public void PollingModeReloadsOnChange()
        {
            var path = _dir.PathOf("overrides.json");
            Write(path, @"{""flagValues"": {""flag1"": true}}");

            var source = StartSource(new[] { path }, changeDetection: FileOverrideTypes.ChangeDetection.Polling);
            Assert.IsType<FileDataPoller>(source.ChangeDetector);
            RequireInitialSnapshot();

            Write(path, @"{""flagValues"": {""flag1"": true, ""flag2"": false}}");
            // Make the rewrite observable through (modification time, size) whatever the timestamp granularity.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
            Assert.Equal(2, FlagsByKey(RequireSnapshot()).Count);
        }

        [Fact]
        public void RetainsLastGoodDataAcrossAMalformedEdit()
        {
            var path = _dir.PathOf("overrides.json");
            Write(path, @"{""flagValues"": {""flag1"": true}}");

            StartSource(new[] { path }, changeDetection: FileOverrideTypes.ChangeDetection.Watching);
            RequireInitialSnapshot();

            // A malformed edit produces no snapshot. The previously applied overrides stay in effect
            // because the sink is never called. The failure is logged.
            Write(path, @"{""flagValues""");
            RequireNoSnapshot(TimeSpan.FromMilliseconds(300));
            AssertLogMessageRegex(true, LogLevel.Error, "Unable to load flags: error parsing file");

            // Fixing the file recovers, through the change notification or the failure retry.
            Write(path, @"{""flagValues"": {""flag1"": false}}");
            var flags = FlagsByKey(RequireSnapshot());
            Assert.Equal(new[] { LdValue.Of(false) }, flags["flag1"].Variations);
        }

        [Fact]
        public void MalformedInitialFileLeavesTheLayerEmptyUntilFixed()
        {
            var path = _dir.PathOf("overrides.json");
            Write(path, @"{""flagValues""");

            StartSource(new[] { path });

            RequireNoSnapshot(TimeSpan.FromMilliseconds(100));
            AssertLogMessageRegex(true, LogLevel.Error, "Unable to load flags");

            Write(path, @"{""flagValues"": {""flag1"": true}}");
            Assert.Single(FlagsByKey(RequireSnapshot()));
        }

        [Fact]
        public void WatchingModeWithAMissingDirectoryLogsAndStillLoads()
        {
            var path = Path.Combine(_dir.PathOf("no-such-directory"), "overrides.json");

            StartSource(new[] { path }, changeDetection: FileOverrideTypes.ChangeDetection.Watching);

            Assert.Empty(FlagsByKey(RequireInitialSnapshot()));
            AssertLogMessageRegex(true, LogLevel.Error, "Unable to watch override files");
        }

        [Fact]
        public void DisposeIsIdempotentAndStopsReloads()
        {
            var path = _dir.PathOf("overrides.json");
            Write(path, @"{""flagValues"": {""flag1"": true}}");

            var source = StartSource(new[] { path });
            RequireInitialSnapshot();

            source.Dispose();
            source.Dispose();

            Write(path, @"{""flagValues"": {""flag1"": false}}");
            RequireNoSnapshot(TimeSpan.FromMilliseconds(300));
        }
    }
}
