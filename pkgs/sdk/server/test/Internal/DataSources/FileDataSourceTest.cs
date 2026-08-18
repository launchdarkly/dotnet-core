using System;
using System.Collections.Concurrent;
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

                        _updateSink.Inits.ExpectNoValue();
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

                    AssertHelpers.ExpectPredicate(_updateSink.Inits, IsSegmentOnlyDataAfterReload,
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

        [Fact]
        public void ParseFailureFromPartialReadIsRetriedUntilContentIsComplete()
        {
            var reader = new ScriptedFileReader();
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true).FileReader(reader);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    WaitForReads(reader, 2);
                    reader.Bad = false; // as if the write completed, with no further notification
                    _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
                    Assert.True(fp.Initialized);
                }
            }
        }

        [Fact]
        public void ParseRetryStopsAfterMaxAttemptsAndDoesNotInit()
        {
            var reader = new ScriptedFileReader();
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true).FileReader(reader);
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
        }

        [Fact]
        public void ParseRetryBudgetResetsForANewFailureEpisode()
        {
            var reader = new ScriptedFileReader();
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true).FileReader(reader);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    WaitForReads(reader, 5); // episode 1: all attempts fail
                    Thread.Sleep(1500);
                    Assert.Equal(5, reader.Reads); // episode 1 exhausted, nothing pending

                    // A new file-change notification starts a new episode with a fresh retry
                    // budget, even though its first read still sees partial content.
                    file.SetContent("trigger-new-episode");
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
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true).FileReader(reader)
                    .Parser(s => yaml.Deserialize<object>(s));
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    reader.Bad = false;
                    _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
                }
            }
        }

        [Fact]
        public void ParseFailureIsNotRetriedIfAutoUpdateIsOff()
        {
            var reader = new ScriptedFileReader();
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(false).FileReader(reader);
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
        }

        [Fact]
        public void TransientReadErrorDuringRetryDoesNotEndTheEpisode()
        {
            var reader = new ScriptedFileReader();
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true).FileReader(reader);
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
        }

        [Fact]
        public void PendingParseRetryIsCanceledByDispose()
        {
            var reader = new ScriptedFileReader();
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true).FileReader(reader);
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
        }

        // The multi-path tests below use paths in a directory that does not exist, so the file
        // watcher fails to construct (logged and swallowed, _reloader == null). That makes the
        // load sequence fully deterministic: the only externally triggered loads are the Start()
        // calls, which exercise the same code path as a file-change notification.
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
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true).FileReader(reader);
                using (var fp = MakeDataSource())
                {
                    fp.Start(); // fails, schedules a retry

                    // An externally triggered load (same code path as a file-change notification)
                    // succeeds before the pending retry fires.
                    reader.Bad = false;
                    fp.Start();
                    _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(1));
                    var readsAfterSuccess = reader.Reads;

                    Thread.Sleep(1500); // past the retry delay
                    // The pending retry saw that the load already succeeded and did not reload,
                    // so no redundant Init (which would fire spurious change events) occurred.
                    Assert.Equal(readsAfterSuccess, reader.Reads);
                    _updateSink.Inits.ExpectNoValue();
                }
            }
        }

        [Fact]
        public void ParseFailureInANewEpisodeGetsAFullRetryBudgetAfterAnotherPathGaveUp()
        {
            var reader = new PerPathScriptedReader();
            reader.SetBad(MultiPathB, true);
            factory.FilePaths(MultiPathA, MultiPathB).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start(); // A parses, B fails; B accumulates failures across retries
                WaitUntil(() => reader.Reads(MultiPathB) >= 4, "4 reads of path B");
                reader.SetBad(MultiPathA, true); // now every attempt stops at A
                WaitUntil(() => reader.Reads(MultiPathA) >= 9, "A to exhaust its retry budget");
                Thread.Sleep(1500); // the retry chain is dead
                Assert.Equal(9, reader.Reads(MultiPathA));

                // A new externally triggered load fails at A; A recovers before the retry fires.
                fp.Start();
                reader.SetBad(MultiPathA, false);
                WaitUntil(() => reader.Reads(MultiPathB) >= 5, "the retry to reach path B");
                reader.SetBad(MultiPathB, false);

                // B's failure was its first in the new episode, so it gets a fresh retry budget
                // instead of inheriting the dead episode's count and giving up immediately.
                _updateSink.Inits.ExpectValue(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public void PathAbandonedWhenAnotherPathGivesUpGetsATerminalLogMessage()
        {
            var reader = new PerPathScriptedReader();
            reader.SetBad(MultiPathB, true);
            factory.FilePaths(MultiPathA, MultiPathB).AutoUpdate(true).FileReader(reader);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                WaitUntil(() => reader.Reads(MultiPathB) >= 4, "4 reads of path B");
                reader.SetBad(MultiPathA, true);
                WaitUntil(() => reader.Reads(MultiPathA) >= 9, "A to exhaust its retry budget");
                Thread.Sleep(1500); // the retry chain is dead

                // B was last warned "will retry in 600 ms", but A's give-up ended the chain.
                // That promise must be either fulfilled (B re-read) or terminated with an
                // error log naming B.
                var bRetried = reader.Reads(MultiPathB) >= 5;
                var bTerminallyLogged = LogCapture.GetMessages().Any(m =>
                    m.Level == LogLevel.Error && m.Text.Contains(MultiPathB));
                Assert.True(bRetried || bTerminallyLogged,
                    "path B was promised a retry but was never re-read and got no terminal error log");
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

        // Predicate that matches the structure of segment-only.json reloaded after the initial load.
        // We deliberately don't pin the exact version: with the file watcher firing on truncate-then-write
        // and the JsonException retry, the version of the successful load is non-deterministic, so we
        // only require that it isn't the initial version 1.
        private static bool IsSegmentOnlyDataAfterReload(FullDataSet<ItemDescriptor> actual)
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
            if (segmentDescriptor.Key != "seg1" || segmentDescriptor.Value.Version == 1)
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
