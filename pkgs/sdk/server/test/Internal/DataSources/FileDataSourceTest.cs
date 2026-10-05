using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Castle.Core.Internal;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using YamlDotNet.Serialization;
using Xunit;
using Xunit.Abstractions;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.TestUtils;
using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    public class FileDataSourceTest : BaseTest
    {
        private static readonly string ALL_DATA_JSON_FILE = TestUtils.TestFilePath("all-properties.json");
        private static readonly string ALL_DATA_YAML_FILE = TestUtils.TestFilePath("all-properties.yml");

        private readonly CapturingDataSourceUpdates _updateSink = new CapturingDataSourceUpdates();
        private readonly FileDataSourceBuilder factory = FileData.DataSource();
        private readonly Context user = Context.New("key");

        public FileDataSourceTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        private IDataSource MakeDataSource() =>
            factory.Build(BasicContext.WithDataSourceUpdates(_updateSink));

        [Fact]
        public void FlagsAreNotLoadedUntilStart()
        {
            factory.FilePaths(ALL_DATA_JSON_FILE);
            using (var fp = MakeDataSource())
            {
                _updateSink.Inits.ExpectNoValue();
            }
        }

        [Fact]
        public void FlagsAreLoadedOnStart()
        {
            factory.FilePaths(ALL_DATA_JSON_FILE);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                var initData = _updateSink.Inits.ExpectValue();
                AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFullDataFile(1)), DataSetAsJson(initData));
            }
        }

        [Fact]
        public void FlagsCanBeLoadedWithExternalYamlParser()
        {
            var yaml = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
            factory.FilePaths(ALL_DATA_YAML_FILE)
                .Parser(s => yaml.Deserialize<object>(s));
            using (var fp = MakeDataSource())
            {
                fp.Start();
                var initData = _updateSink.Inits.ExpectValue();
                AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFullDataFile(1)), DataSetAsJson(initData));
            }
        }

        [Fact]
        public void StartTaskIsCompletedAndInitializedIsTrueAfterSuccessfulLoad()
        {
            using (var fp = MakeDataSource())
            {
                var task = fp.Start();
                Assert.True(task.IsCompleted);
                Assert.True(fp.Initialized);
            }
        }

        [Fact]
        public void StartTaskIsCompletedAndInitializedIsFalseAfterFailedLoadDueToMissingFile()
        {
            factory.FilePaths(ALL_DATA_JSON_FILE, "bad-file-path");
            using (var fp = MakeDataSource())
            {
                var task = fp.Start();
                Assert.True(task.IsCompleted);
                Assert.False(fp.Initialized);
            }
        }

        [Fact]
        public void CanIgnoreMissingFileOnStartup()
        {
            factory.FilePaths(ALL_DATA_JSON_FILE, "bad-file-path").SkipMissingPaths(true);
            using (var fp = MakeDataSource())
            {
                var task = fp.Start();
                Assert.True(task.IsCompleted);
                Assert.True(fp.Initialized);
                var initData = _updateSink.Inits.ExpectValue();
                AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFullDataFile(1)), DataSetAsJson(initData));
            }
        }

        [Fact]
        public void StartTaskIsCompletedAndInitializedIsFalseAfterFailedLoadDueToMalformedFile()
        {
            factory.FilePaths(TestUtils.TestFilePath("bad-file.txt"));
            using (var fp = MakeDataSource())
            {
                var task = fp.Start();
                Assert.True(task.IsCompleted);
                Assert.False(fp.Initialized);
            }
        }

        [Fact]
        public void ModifiedFileIsNotReloadedIfAutoUpdateIsOff()
        {
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path);
                file.SetContentFromPath(TestUtils.TestFilePath("flag-only.json"));
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    var initData = _updateSink.Inits.ExpectValue();

                    file.SetContentFromPath(TestUtils.TestFilePath("segment-only.json"));
                    _updateSink.Inits.ExpectNoValue();
                }
            }
        }

        [Fact]
        public void ModifiedFileIsReloadedIfAutoUpdateIsOn()
        {
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true);
                file.SetContentFromPath(TestUtils.TestFilePath("flag-only.json"));
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    var initData = _updateSink.Inits.ExpectValue();
                    AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFlagOnlyFile(1)), DataSetAsJson(initData));
                    Thread.Sleep(100);

                    file.SetContentFromPath(TestUtils.TestFilePath("segment-only.json"));

                    AssertHelpers.ExpectPredicate(_updateSink.Inits, IsSegmentOnlyDataAfterReload,
                        "Did not receive expected update from the file data source.",
                        TimeSpan.FromSeconds(30));
                }
            }
        }

        [Fact]
        public void FlagChangeEventIsGeneratedWhenModifiedFileIsReloaded()
        {
            using (var file = TempFile.Create())
            {
                file.SetContent(@"{""flagValues"":{""flag1"":""a""}}");

                var config = BasicConfig()
                    .DataSource(FileData.DataSource().FilePaths(file.Path).AutoUpdate(true))
                    .Build();

                using (var client = new LdClient(config))
                {
                    var events = new EventSink<FlagChangeEvent>();
                    client.FlagTracker.FlagChanged += events.Add;

                    file.SetContent(@"{""flagValues"":{""flag1"":""b""}}");

                    var e = events.ExpectValue(TimeSpan.FromSeconds(5));
                    Assert.Equal("flag1", e.Key);
                    Assert.Equal("b", client.StringVariation("flag1", user, ""));
                }
            }
        }

        [Fact]
        public void ModifiedFileIsNotReloadedIfOneFileIsMissing()
        {
            using (var file1 = TempFile.Create())
            {
                using (var file2 = TempFile.Create())
                {
                    factory.FilePaths(file1.Path, file2.Path)
                        .AutoUpdate(true);
                    file1.SetContentFromPath(TestUtils.TestFilePath("flag-only.json"));
                    file2.SetContent("{}");
                    using (var fp = MakeDataSource())
                    {
                        fp.Start();
                        var initData = _updateSink.Inits.ExpectValue();
                        AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFlagOnlyFile(1)), DataSetAsJson(initData));

                        file2.Delete();
                        file1.SetContentFromPath(TestUtils.TestFilePath("segment-only.json"));

                        // The reload runs after the 100 ms settle window, which the default 100 ms
                        // observation window equaled (implementation detail): wait for the reload to
                        // fail first, then observe.
                        WaitUntil(() => LogCapture.HasMessageWithRegex(LogLevel.Error,
                            "^Unable to load flags: unable to read file"), "the failed reload to be logged");
                        _updateSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(500));
                    }
                }
            }
        }

        [Fact]
        public void ModifiedFileIsReloadedEvenIfOneFileIsMissingIfSkipMissingPathsIsSet()
        {
            using (var file1 = TempFile.Create())
            {
                var filename2 = TempFile.MakePathOfNonexistentFile();
                factory.FilePaths(file1.Path, filename2)
                    .SkipMissingPaths(true)
                    .AutoUpdate(true);
                file1.SetContentFromPath(TestUtils.TestFilePath("flag-only.json"));
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    var initData = _updateSink.Inits.ExpectValue();
                    AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFlagOnlyFile(1)), DataSetAsJson(initData));

                    file1.SetContentFromPath(TestUtils.TestFilePath("segment-only.json"));

                    AssertHelpers.ExpectPredicate(_updateSink.Inits, IsSegmentOnlyDataAfterReload,
                        "Did not receive expected update from the file data source.",
                        TimeSpan.FromSeconds(30));
                }
            }
        }

        [Fact]
        public void IfFlagsAreBadAtStartTimeAutoUpdateCanStillLoadGoodDataLater()
        {
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true);
                file.SetContent("{not correct}");
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    _updateSink.Inits.ExpectNoValue();

                    file.SetContentFromPath(TestUtils.TestFilePath("segment-only.json"));

                    // The good data is the first load that succeeded, so its version is 1: a load
                    // that fails no longer consumes a version (a behavior change in the PR body).
                    AssertHelpers.ExpectPredicate(_updateSink.Inits,
                        data => IsSegmentOnlyData(data, version => version == 1),
                        "Did not receive expected update from the file data source.",
                        TimeSpan.FromSeconds(30));
                }
            }
        }

        private const string ValidFlagJson = @"{""flagValues"":{""flag1"":""a""}}";
        private const string TruncatedFlagJson = @"{""flagValues"": {"; // invalid as JSON and as YAML

        // Simulates reading a file that is mid-write: returns truncated content until Bad is
        // cleared, and counts reads so tests can observe retry attempts deterministically
        // without depending on real file-watcher timing.
        private class ScriptedFileReader : FileDataTypes.IFileReader
        {
            private int _reads;
            public volatile bool Bad = true;
            public volatile bool Throw = false;
            public int Reads => Volatile.Read(ref _reads);

            public string ReadAllText(string path)
            {
                Interlocked.Increment(ref _reads);
                if (Throw)
                {
                    throw new IOException("simulated transient read error");
                }
                return Bad ? TruncatedFlagJson : ValidFlagJson;
            }
        }

        private static void WaitUntil(Func<bool> condition, string description)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
            Assert.True(condition(), "timed out waiting for " + description);
        }

        private static void WaitForReads(ScriptedFileReader reader, int count) =>
            WaitUntil(() => reader.Reads >= count, count + " file reads");

        // A path in a directory that does not exist. The watcher has nothing to watch there (it logs
        // that and keeps trying), so no change notification can start a load: the only loads are the
        // Start() calls and the retries. The tests whose reader ignores the path use it instead of a
        // file in the shared temp directory, whose activity can reach the watcher on Windows and macOS.
        private const string NoNotificationsPath = "/nonexistent-ld-filedatasource-test-dir/data.json";

        [Fact]
        public void ParseFailureFromPartialReadIsRetriedUntilContentIsComplete()
        {
            var reader = new ScriptedFileReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                WaitForReads(reader, 2);
                reader.Bad = false; // as if the write completed, with no further notification
                _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
                Assert.True(fp.Initialized);
            }
        }

        [Fact]
        public void ParseRetryStopsAfterMaxAttemptsAndDoesNotInit()
        {
            var reader = new ScriptedFileReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                WaitForReads(reader, 5); // initial attempt + 4 retries
                Thread.Sleep(1500); // longer than two retry delays
                Assert.Equal(5, reader.Reads); // budget exhausted, no further attempts
                _updateSink.Inits.ExpectNoValue();
                Assert.False(fp.Initialized);
            }
        }

        [Fact]
        public void RetryFollowsTheFailureAfter600Milliseconds()
        {
            var reader = new ScriptedFileReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                var stopwatch = Stopwatch.StartNew();
                fp.Start(); // fails; the retry is due in 600 ms
                WaitForReads(reader, 2);
                // The documented retry delay, with room for the polling and scheduling jitter.
                Assert.InRange(stopwatch.ElapsedMilliseconds, 450, 900);
            }
        }

        [Fact]
        public void ParseRetryBudgetResetsForANewFailureEpisode()
        {
            var reader = new ScriptedFileReader();
            // A private directory: the shared temp directory's activity can reach the watcher on
            // Windows and macOS and start a run of attempts early. The file is created by the
            // notification below, so nothing is written before the watcher starts.
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                factory.FilePaths(path).AutoUpdate(true).FileReader(reader);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    WaitForReads(reader, 5); // episode 1: all attempts fail
                    Thread.Sleep(1500);
                    Assert.Equal(5, reader.Reads); // episode 1 exhausted, nothing pending

                    // A new file-change notification starts a new episode with a fresh retry
                    // budget, even though its first read still sees partial content.
                    File.WriteAllText(path, "trigger-new-episode");
                    WaitForReads(reader, 6);

                    reader.Bad = false; // write completed; no further notification arrives
                    _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
                }
            }
        }

        [Fact]
        public void ParseRetryAppliesWhenAlternateParserIsConfigured()
        {
            var yaml = new DeserializerBuilder().Build();
            var reader = new ScriptedFileReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(true).FileReader(reader)
                .Parser(s => yaml.Deserialize<object>(s));
            using (var fp = MakeDataSource())
            {
                fp.Start();
                reader.Bad = false;
                _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public void ParseFailureIsNotRetriedIfAutoUpdateIsOff()
        {
            var reader = new ScriptedFileReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(false).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                var task = fp.Start();
                Assert.True(task.IsCompleted);
                Assert.False(fp.Initialized);
                reader.Bad = false;
                _updateSink.Inits.ExpectNoValue(TimeSpan.FromSeconds(2));
                Assert.False(fp.Initialized);
                Assert.Equal(1, reader.Reads);
            }
        }

        [Fact]
        public void TransientReadErrorDuringRetryDoesNotEndTheEpisode()
        {
            var reader = new ScriptedFileReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start(); // parse fails, schedules a retry
                // The retry's read fails transiently (e.g. a writer is replacing the file);
                // the content itself is complete from here on.
                reader.Bad = false;
                reader.Throw = true;
                WaitForReads(reader, 2);
                reader.Throw = false;

                // The episode still has budget, so the chain must continue and load the data
                // instead of dying on the non-parse failure.
                _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public void PendingParseRetryIsCanceledByDispose()
        {
            var reader = new ScriptedFileReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start(); // schedules a retry
                fp.Dispose();
                // No new load may start after Dispose; any retry scheduled before it must
                // observe the disposal and do nothing. (Reads are captured after Dispose so
                // the test stays valid even if a retry fired before Dispose ran.)
                var readsAtDispose = reader.Reads;
                Thread.Sleep(1500);
                Assert.Equal(readsAtDispose, reader.Reads);
            }
        }

        // Serves truncated content on the first read, holds the second read for longer than the
        // retry delay, and serves complete content from then on.
        private class SlowSecondReadReader : FileDataTypes.IFileReader
        {
            private int _reads;
            public int Reads => Volatile.Read(ref _reads);

            public string ReadAllText(string path)
            {
                var n = Interlocked.Increment(ref _reads);
                if (n == 1)
                {
                    return TruncatedFlagJson;
                }
                if (n == 2)
                {
                    Thread.Sleep(1200); // longer than the 600 ms retry delay
                }
                return ValidFlagJson;
            }
        }

        [Fact]
        public void RetryThatFiresDuringASuccessfulStartDoesNotInitAgain()
        {
            // A retry whose timer fires while a Start is loading waits for that load. Once the load
            // has succeeded there is nothing to retry, so the data is not applied again, which would
            // fire a change event for every flag.
            var reader = new SlowSecondReadReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                Assert.False(fp.Start().Result); // fails; a retry is due in 600 ms
                Assert.True(fp.Start().Result);  // its read spans the retry deadline, then succeeds
                _updateSink.Inits.ExpectValue();
                _updateSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(1500));
                Assert.Equal(2, reader.Reads);
            }
        }

        // Two paths in the directory of NoNotificationsPath, so that the only loads are the Start()
        // calls, which start a new run of attempts as a notification does, and the retries.
        private const string MultiPathA = "/nonexistent-ld-filedatasource-test-dir/a.json";
        private const string MultiPathB = "/nonexistent-ld-filedatasource-test-dir/b.json";

        private class PerPathScriptedReader : FileDataTypes.IFileReader
        {
            private readonly ConcurrentDictionary<string, int> _reads = new ConcurrentDictionary<string, int>();
            private readonly ConcurrentDictionary<string, bool> _bad = new ConcurrentDictionary<string, bool>();

            public void SetBad(string path, bool bad) { _bad[path] = bad; }
            public int Reads(string path) => _reads.TryGetValue(path, out var n) ? n : 0;

            public string ReadAllText(string path)
            {
                _reads.AddOrUpdate(path, 1, (_, n) => n + 1);
                if (_bad.TryGetValue(path, out var bad) && bad)
                {
                    return TruncatedFlagJson;
                }
                // distinct flag keys per path, so a successful merge of both files can't throw
                // on duplicate keys (the builder default is DuplicateKeysHandling.Throw)
                return path == MultiPathA
                    ? @"{""flagValues"":{""flagA"":""a""}}"
                    : @"{""flagValues"":{""flagB"":""b""}}";
            }
        }

        [Fact]
        public void PendingParseRetryIsSkippedIfAnExternalReloadAlreadySucceeded()
        {
            var reader = new ScriptedFileReader();
            factory.FilePaths(NoNotificationsPath).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start(); // fails, schedules a retry

                // A second Start loads synchronously and succeeds before the pending retry fires.
                reader.Bad = false;
                fp.Start();
                _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(1));
                var readsAfterSuccess = reader.Reads;

                Thread.Sleep(1500); // past the retry delay
                // The success disarmed the pending retry, so no redundant Init (which would fire
                // spurious change events) occurred.
                Assert.Equal(readsAfterSuccess, reader.Reads);
                _updateSink.Inits.ExpectNoValue();
            }
        }

        [Fact]
        public void RetryBudgetIsPerEpisodeNotPerPath()
        {
            // This replaces the pins of a retry budget per path, under which a run of attempts could
            // exceed the budget when the failing path changed, and of the per-path messages when it
            // ended (a behavior change in the PR body). The budget now covers the whole run.
            var reader = new PerPathScriptedReader();
            reader.SetBad(MultiPathB, true);
            factory.FilePaths(MultiPathA, MultiPathB).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start(); // A parses, B fails
                WaitUntil(() => reader.Reads(MultiPathB) >= 3, "3 reads of path B");
                reader.SetBad(MultiPathA, true); // now every attempt stops at A

                // The run has five attempts in total, however the failing path changes, and its end
                // is logged once.
                WaitUntil(() => reader.Reads(MultiPathA) >= 5, "A to be read on the fifth attempt");
                _updateSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(1500)); // longer than two retry delays
                Assert.Equal(5, reader.Reads(MultiPathA));
                Assert.Equal(3, reader.Reads(MultiPathB));
                AssertLogMessageRegex(true, LogLevel.Error, "after 5 attempts");

                // A new externally triggered load is a new run with a fresh budget: it fails at B,
                // and the retry loads the data once B recovers.
                reader.SetBad(MultiPathA, false);
                fp.Start();
                WaitUntil(() => reader.Reads(MultiPathB) >= 5, "the retry to reach path B");
                reader.SetBad(MultiPathB, false);
                _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public void FullFlagDefinitionEvaluatesAsExpected()
        {
            factory.FilePaths(ALL_DATA_JSON_FILE);
            var config1 = BasicConfig().DataSource(factory).Build();
            using (var client = new LdClient(config1))
            {
                Assert.Equal("on", client.StringVariation("flag1", user, ""));
            }
        }

        [Fact]
        public void SimplifiedFlagEvaluatesAsExpected()
        {
            factory.FilePaths(ALL_DATA_JSON_FILE);
            var config1 = BasicConfig().DataSource(factory).Build();
            using (var client = new LdClient(config1))
            {
                Assert.Equal("value2", client.StringVariation("flag2", user, ""));
            }
        }

        // Serves whatever content the test sets and counts the reads, so a test can make a load
        // fail or succeed at will without touching the file system.
        private class ContentFileReader : FileDataTypes.IFileReader
        {
            private int _reads;
            public volatile string Content;
            public int Reads => Volatile.Read(ref _reads);

            public string ReadAllText(string path)
            {
                Interlocked.Increment(ref _reads);
                return Content;
            }
        }

        private static ItemDescriptor FlagItem(FullDataSet<ItemDescriptor> data, string key) =>
            data.Data.First(kv => kv.Key == DataModel.Features).Value.Items.First(kv => kv.Key == key).Value;

        [Fact]
        public void EveryItemGetsTheVersionOfItsLoadAndTheVersionCountsSuccessfulLoads()
        {
            var reader = new ContentFileReader { Content = File.ReadAllText(ALL_DATA_JSON_FILE) };
            factory.FilePaths("any-path").FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFullDataFile(1)),
                    DataSetAsJson(_updateSink.Inits.ExpectValue()));

                // A load that fails does not advance the version.
                reader.Content = TruncatedFlagJson;
                fp.Start();
                _updateSink.Inits.ExpectNoValue();

                // The next successful load stamps every flag, segment, and descriptor with the next
                // version, whatever version the file gives them.
                reader.Content = File.ReadAllText(ALL_DATA_JSON_FILE);
                fp.Start();
                var data = _updateSink.Inits.ExpectValue();
                AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFullDataFile(2)), DataSetAsJson(data));
                foreach (var kind in data.Data)
                {
                    Assert.All(kind.Value.Items, kv => Assert.Equal(2, kv.Value.Version));
                }
            }
        }

        [Fact]
        public void IdenticalContentIsAppliedAgainByEachStart()
        {
            factory.FilePaths(ALL_DATA_JSON_FILE);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFullDataFile(1)),
                    DataSetAsJson(_updateSink.Inits.ExpectValue()));
                fp.Start();
                AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFullDataFile(2)),
                    DataSetAsJson(_updateSink.Inits.ExpectValue()));
            }
        }

        [Fact]
        public void FlagValuesEntryBecomesAFlagThatIsOnAndServesItsOneVariationAsFallthrough()
        {
            var reader = new ContentFileReader { Content = ValidFlagJson };
            factory.FilePaths("any-path").FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                var flag = Assert.IsType<FeatureFlag>(FlagItem(_updateSink.Inits.ExpectValue(), "flag1").Item);
                Assert.Equal("flag1", flag.Key);
                Assert.Equal(1, flag.Version);
                Assert.True(flag.On);
                Assert.Equal(new[] { LdValue.Of("a") }, flag.Variations);
                Assert.Equal(0, flag.Fallthrough.Variation);
                Assert.Null(flag.Fallthrough.Rollout);
                Assert.Null(flag.OffVariation);
                Assert.Empty(flag.Rules);
                Assert.Empty(flag.Prerequisites);
                Assert.Empty(flag.Targets);
            }
        }

        [Fact]
        public void DuplicateKeyAcrossFilesWithIgnoreKeepsTheFirstOccurrence()
        {
            using (var file1 = TempFile.Create())
            using (var file2 = TempFile.Create())
            {
                file1.SetContent(@"{""flagValues"":{""flag1"":""a""}}");
                file2.SetContent(@"{""flagValues"":{""flag1"":""b""}}");
                factory.FilePaths(file1.Path, file2.Path)
                    .DuplicateKeysHandling(FileDataTypes.DuplicateKeysHandling.Ignore);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    Assert.True(fp.Initialized);
                    var flag = Assert.IsType<FeatureFlag>(
                        FlagItem(_updateSink.Inits.ExpectValue(), "flag1").Item);
                    Assert.Equal(new[] { LdValue.Of("a") }, flag.Variations);
                }
            }
        }

        [Fact]
        public void LoadWithNoPathsAppliesAnEmptyDataSetWithBothKinds()
        {
            using (var fp = MakeDataSource())
            {
                fp.Start();
                var data = _updateSink.Inits.ExpectValue();
                Assert.Equal(new[] { DataModel.Features, DataModel.Segments },
                    data.Data.Select(kv => kv.Key).OrderBy(kind => kind.Name));
                Assert.All(data.Data, kv => Assert.Empty(kv.Value.Items));
            }
        }

        [Fact]
        public void AutoUpdateOffMeansNoWatcherAndNoRetry()
        {
            var reader = new ScriptedFileReader();
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).FileReader(reader);
                using (var fp = MakeDataSource())
                {
                    fp.Start(); // fails to parse
                    Assert.False(fp.Initialized);

                    // The content is good from here on, but nothing reads it: the file is not
                    // watched, and the failure is not retried.
                    reader.Bad = false;
                    file.SetContent("changed");
                    // Longer than two retry delays.
                    _updateSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(1500));
                    Assert.Equal(1, reader.Reads);
                    Assert.False(fp.Initialized);
                }
            }
        }

        [Fact]
        public void StartAfterDisposeDoesNothingAndReturnsTheLastResult()
        {
            var reader = new ContentFileReader { Content = ValidFlagJson };
            factory.FilePaths("any-path").AutoUpdate(true).FileReader(reader);
            var fp = MakeDataSource();
            Assert.True(fp.Start().Result);
            _updateSink.Inits.ExpectValue();

            fp.Dispose();
            fp.Dispose();
            var task = fp.Start();
            Assert.True(task.IsCompleted);
            Assert.True(task.Result);
            Assert.True(fp.Initialized);
            _updateSink.Inits.ExpectNoValue();
            Assert.Equal(1, reader.Reads);
        }

        [Fact]
        public void DeletingAWatchedFileWithSkipMissingPathsRemovesItsItems()
        {
            // A deleted file is a change (a behavior change in the PR body). With SkipMissingPaths,
            // the remaining files are applied, so the deleted file's items leave the store.
            using (var dir = TempDirectory.Create())
            {
                var flagFile = dir.PathOf("flags.json");
                var segmentFile = dir.PathOf("segments.json");
                File.Copy(TestUtils.TestFilePath("flag-only.json"), flagFile);
                File.Copy(TestUtils.TestFilePath("segment-only.json"), segmentFile);
                factory.FilePaths(flagFile, segmentFile).AutoUpdate(true).SkipMissingPaths(true);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    var initData = _updateSink.Inits.ExpectValue();
                    Assert.Single(initData.Data.First(kv => kv.Key == DataModel.Features).Value.Items);
                    Assert.Single(initData.Data.First(kv => kv.Key == DataModel.Segments).Value.Items);

                    File.Delete(segmentFile);
                    AssertHelpers.ExpectPredicate(_updateSink.Inits, IsFlagOnlyDataAfterReload,
                        "Did not receive the data set without the deleted file's items.",
                        TimeSpan.FromSeconds(10));
                }
            }
        }

        [Fact]
        public void DeletingAWatchedFileWithoutSkipMissingPathsFailsTheReloadAndKeepsTheData()
        {
            // A deleted file is a change (a behavior change in the PR body). Without
            // SkipMissingPaths, the reload fails and is logged; the store keeps its data.
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                File.Copy(TestUtils.TestFilePath("flag-only.json"), path);
                factory.FilePaths(path).AutoUpdate(true);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    _updateSink.Inits.ExpectValue();

                    File.Delete(path);
                    WaitUntil(
                        () => LogCapture.HasMessageWithRegex(LogLevel.Error,
                            "^Unable to load flags: unable to read file: .*data.json"),
                        "the failed reload to be logged");
                    _updateSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(500));
                    Assert.True(fp.Initialized);
                }
            }
        }

        [Fact]
        public void FileInADirectoryCreatedAfterStartIsLoadedWhenAutoUpdateIsOn()
        {
            // A directory that does not exist yet is watched once it appears (behavior change D8 in
            // the PR body), instead of disabling auto-update for every path. The load fails until
            // then, as a path in a missing directory always has.
            using (var dir = TempDirectory.Create())
            {
                var subdir = dir.PathOf("created-later");
                var path = Path.Combine(subdir, "data.json");
                factory.FilePaths(path).AutoUpdate(true);
                using (var fp = MakeDataSource())
                {
                    Assert.False(fp.Start().Result);
                    // The retries of the failed start end before the directory appears, so only the
                    // watcher can load the file.
                    WaitUntil(() => LogCapture.HasMessageWithRegex(LogLevel.Error, "after 5 attempts"),
                        "the retry budget to end");

                    Directory.CreateDirectory(subdir);
                    File.Copy(TestUtils.TestFilePath("segment-only.json"), path);
                    AssertHelpers.ExpectPredicate(_updateSink.Inits,
                        data => IsSegmentOnlyData(data, version => version == 1),
                        "Did not receive the data of the file in the new directory.",
                        TimeSpan.FromSeconds(10));
                    Assert.True(fp.Initialized);
                }
            }
        }

        [Fact]
        public void BurstOfChangeNotificationsLoadsOnce()
        {
            // One edit produces several notifications. They settle for 100 ms before one reload
            // (behavior change D2 in the PR body); the reader counts the loads.
            var reader = new ScriptedFileReader { Bad = false };
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                File.WriteAllText(path, ValidFlagJson);
                factory.FilePaths(path).AutoUpdate(true).FileReader(reader);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    _updateSink.Inits.ExpectValue();
                    var readsAfterStart = reader.Reads;

                    for (var i = 0; i < 10; i++)
                    {
                        File.WriteAllText(path, ValidFlagJson);
                        Thread.Sleep(5);
                    }

                    // One reload for the burst, with a tolerance for a notification that arrives
                    // during the reload and gets one follow-up, or a stale notification. The quiet
                    // period lets such a follow-up finish before the loads are counted.
                    _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
                    Thread.Sleep(500);
                    Assert.InRange(reader.Reads - readsAfterStart, 1, 3);
                }
            }
        }

        [Fact]
        public void DisposeStopsTheWatcher()
        {
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                File.WriteAllText(path, ValidFlagJson);
                factory.FilePaths(path).AutoUpdate(true);
                var fp = MakeDataSource();
                fp.Start();
                _updateSink.Inits.ExpectValue();
                fp.Dispose();

                // A watcher that outlived Dispose would notice the removal of its directory on its
                // next check, within a second, and log it.
                Directory.Delete(dir.Path, true);
                Thread.Sleep(2500); // more than two watcher check intervals
                AssertLogMessageRegex(false, LogLevel.Warn, "no longer exists");
            }
        }

        [Fact]
        public void StartAfterDisposeStartsNoWatcher()
        {
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                File.WriteAllText(path, ValidFlagJson);
                factory.FilePaths(path).AutoUpdate(true);
                var fp = MakeDataSource();
                fp.Start();
                _updateSink.Inits.ExpectValue();
                fp.Dispose();
                fp.Start();

                // A watcher set up by the Start after Dispose would notice the removal of its
                // directory on its next check, within a second, and log it.
                Directory.Delete(dir.Path, true);
                Thread.Sleep(2500); // more than two watcher check intervals
                AssertLogMessageRegex(false, LogLevel.Warn, "no longer exists");
            }
        }

        // Reads the file, and after the first read rewrites it, as a writer racing the initial
        // load would.
        private class RewritesFileAfterFirstReadReader : FileDataTypes.IFileReader
        {
            private readonly string _newContent;
            private int _reads;

            public RewritesFileAfterFirstReadReader(string newContent)
            {
                _newContent = newContent;
            }

            public string ReadAllText(string path)
            {
                var content = File.ReadAllText(path);
                if (Interlocked.Increment(ref _reads) == 1)
                {
                    File.WriteAllText(path, _newContent);
                }
                return content;
            }
        }

        [Fact]
        public void ChangeMadeDuringTheInitialLoadIsLoaded()
        {
            // The watcher is created before the initial load, so a change made while that load runs
            // is still detected.
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                File.WriteAllText(path, @"{""flagValues"":{""flag1"":""a""}}");
                factory.FilePaths(path).AutoUpdate(true)
                    .FileReader(new RewritesFileAfterFirstReadReader(@"{""flagValues"":{""flag1"":""b""}}"));
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    var first = Assert.IsType<FeatureFlag>(
                        FlagItem(_updateSink.Inits.ExpectValue(), "flag1").Item);
                    Assert.Equal(new[] { LdValue.Of("a") }, first.Variations);
                    AssertHelpers.ExpectPredicate(_updateSink.Inits,
                        data => ((FeatureFlag)FlagItem(data, "flag1").Item).Variations
                            .SequenceEqual(new[] { LdValue.Of("b") }),
                        "the change made during the initial load was not loaded", TimeSpan.FromSeconds(5));
                }
            }
        }

        // Delegates to the capturing sink, but throws from the first Init.
        private class ThrowingOnceDataSourceUpdates : IDataSourceUpdates
        {
            private readonly IDataSourceUpdates _inner;
            private bool _thrown;

            public ThrowingOnceDataSourceUpdates(IDataSourceUpdates inner)
            {
                _inner = inner;
            }

            public IDataStoreStatusProvider DataStoreStatusProvider => _inner.DataStoreStatusProvider;

            public bool Init(FullDataSet<ItemDescriptor> allData)
            {
                if (!_thrown)
                {
                    _thrown = true;
                    throw new InvalidOperationException("store failed");
                }
                return _inner.Init(allData);
            }

            public bool Upsert(DataKind kind, string key, ItemDescriptor item) =>
                _inner.Upsert(kind, key, item);

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError) =>
                _inner.UpdateStatus(newState, newError);
        }

        [Fact]
        public void InitThatThrowsDoesNotConsumeAVersion()
        {
            // An Init that throws is a failed load: Start reports it, nothing is remembered, and the
            // next successful load gets the version the failed one would have had.
            factory.FilePaths(ALL_DATA_JSON_FILE);
            var updates = new ThrowingOnceDataSourceUpdates(_updateSink);
            using (var fp = factory.Build(BasicContext.WithDataSourceUpdates(updates)))
            {
                Assert.False(fp.Start().Result);
                AssertLogMessageRegex(true, LogLevel.Error,
                    "^Unable to load flags: error applying file data: .*store failed");
                _updateSink.Inits.ExpectNoValue();

                Assert.True(fp.Start().Result);
                AssertJsonEqual(DataSetAsJson(ExpectedDataSetForFullDataFile(1)),
                    DataSetAsJson(_updateSink.Inits.ExpectValue()));
            }
        }

        private static FullDataSet<ItemDescriptor> ExpectedDataSetForFullDataFile(int version) =>
            new DataSetBuilder()
                .Flags(
                    new FeatureFlagBuilder("flag1").Version(version).On(true).FallthroughVariation(2)
                        .Variations("fall", "off", "on").Build(),
                    new FeatureFlagBuilder("flag2").Version(version).On(true).FallthroughVariation(0)
                        .Variations("value2").Build()
                )
                .Segments(
                    new SegmentBuilder("seg1").Version(version).Included("user1").Build()
                )
                .Build();

        private static FullDataSet<ItemDescriptor> ExpectedDataSetForFlagOnlyFile(int version) =>
            new DataSetBuilder()
                .Flags(
                    new FeatureFlagBuilder("flag1").Version(version).On(true).FallthroughVariation(2)
                        .Variations("fall", "off", "on").Build()
                )
                .Segments()
                .Build();

        private static FullDataSet<ItemDescriptor> ExpectedDataSetForSegmentOnlyFile(int version) =>
            new DataSetBuilder()
                .Flags()
                .Segments(
                    new SegmentBuilder("seg1").Version(version).Included("user1").Build()
                )
                .Build();

        // Predicate that matches the structure of flag-only.json reloaded after the initial load.
        private static bool IsFlagOnlyDataAfterReload(FullDataSet<ItemDescriptor> actual)
        {
            var segments = actual.Data.First(item => item.Key == DataModel.Segments);
            if (!segments.Value.Items.IsNullOrEmpty())
            {
                return false;
            }
            var flagItems = actual.Data.First(item => item.Key == DataModel.Features).Value.Items.ToList();
            return flagItems.Count == 1 && flagItems[0].Key == "flag1" && flagItems[0].Value.Version > 1;
        }

        // Predicate that matches the structure of segment-only.json reloaded after the initial load.
        // We deliberately don't pin the exact version: with the file watcher firing on truncate-then-write,
        // the number of successful reloads is non-deterministic, so we only require that the version
        // isn't the initial version 1.
        private static bool IsSegmentOnlyDataAfterReload(FullDataSet<ItemDescriptor> actual) =>
            IsSegmentOnlyData(actual, version => version != 1);

        // Predicate that matches the structure of segment-only.json with a version the test accepts.
        private static bool IsSegmentOnlyData(FullDataSet<ItemDescriptor> actual, Predicate<int> versionOk)
        {
            var features = actual.Data.First(item => item.Key == DataModel.Features);
            if (!features.Value.Items.IsNullOrEmpty())
            {
                return false;
            }

            var segments = actual.Data.First(item => item.Key == DataModel.Segments);
            var segmentItems = segments.Value.Items.ToList();
            if (segmentItems.Count != 1)
            {
                return false;
            }

            var segmentDescriptor = segmentItems[0];
            if (segmentDescriptor.Key != "seg1" || !versionOk(segmentDescriptor.Value.Version))
            {
                return false;
            }

            if (!(segmentDescriptor.Value.Item is Segment segment) || segment.Deleted)
            {
                return false;
            }

            return segment.Included.Count == 1 && segment.Included[0] == "user1";
        }
    }
}
