using System;
using System.IO;
using System.Linq;
using System.Threading;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    // These tests pin behavior of the file data source that its other tests do not assert directly,
    // so that changes to the shared file loading code cannot alter it: every successful load is
    // applied even when the content is unchanged, the retry budget and its logging, the log messages
    // and levels for a failed load, missing-file handling, and which file system notifications
    // trigger a reload. Where the move onto the shared file loading code changed a pinned message
    // or rule, the test says so and names the behavior change listed in the PR body.
    public class FileDataSourceExistingBehaviorTest : BaseTest
    {
        private static readonly string ALL_DATA_JSON_FILE = TestUtils.TestFilePath("all-properties.json");
        private static readonly string FLAG_ONLY_JSON_FILE = TestUtils.TestFilePath("flag-only.json");
        private static readonly string BAD_FILE = TestUtils.TestFilePath("bad-file.txt");

        private readonly CapturingDataSourceUpdates _updateSink = new CapturingDataSourceUpdates();
        private readonly FileDataSourceBuilder factory = FileData.DataSource();

        public FileDataSourceExistingBehaviorTest(ITestOutputHelper testOutput) : base(testOutput) { }

        private IDataSource MakeDataSource() =>
            factory.Build(BasicContext.WithDataSourceUpdates(_updateSink));

        private static int VersionOfFlag1(FullDataSet<ItemDescriptor> data) =>
            data.Data.First(kv => kv.Key == DataModel.Features).Value.Items.First(kv => kv.Key == "flag1").Value.Version;

        private class ScriptedFileReader : FileDataTypes.IFileReader
        {
            private int _reads;
            public int Reads => Volatile.Read(ref _reads);

            public string ReadAllText(string path)
            {
                Interlocked.Increment(ref _reads);
                return @"{""flagValues"": {"; // invalid as JSON and as YAML
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

        [Fact]
        public void IdenticalContentIsReappliedWithANewVersionOnEachChangeNotification()
        {
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true);
                file.SetContentFromPath(FLAG_ONLY_JSON_FILE);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    Assert.Equal(1, VersionOfFlag1(_updateSink.Inits.ExpectValue()));

                    // A rewrite with the same content is a change notification like any other. The
                    // data is applied again, with the next version number.
                    file.SetContentFromPath(FLAG_ONLY_JSON_FILE);
                    AssertHelpers.ExpectPredicate(_updateSink.Inits, data => VersionOfFlag1(data) > 1,
                        "Did not receive a reload of the unchanged file.", TimeSpan.FromSeconds(30));
                    AssertLogMessage(true, LogLevel.Info, "detected file modification, reloading");
                }
            }
        }

        [Fact]
        public void MissingFileWithoutSkipMissingPathsLogsFailedToLoad()
        {
            factory.FilePaths(ALL_DATA_JSON_FILE, "bad-file-path");
            using (var fp = MakeDataSource())
            {
                fp.Start();
                Assert.False(fp.Initialized);
                _updateSink.Inits.ExpectNoValue();
                // The message text is the shared file loading code's (a behavior change in the PR
                // body); the level and the condition are unchanged.
                AssertLogMessageRegex(true, LogLevel.Error,
                    "^Unable to load flags: unable to read file: .*\\[bad-file-path\\]$");
            }
        }

        [Fact]
        public void EveryFailedStartLogsTheFailureAtError()
        {
            // A load that Start runs is a new event, so its failure is logged at Error even when it
            // is the same failure as the previous Start's; only the automatic retries demote a
            // repeated failure to Debug.
            factory.FilePaths("bad-file-path");
            using (var fp = MakeDataSource())
            {
                Assert.False(fp.Start().Result);
                Assert.False(fp.Start().Result);
                Assert.Equal(2, LogCapture.GetMessages().Count(m =>
                    m.Level == LogLevel.Error && m.Text.Contains("[bad-file-path]")));
            }
        }

        [Fact]
        public void MalformedFileWithAutoUpdateOffLogsFailedToParse()
        {
            factory.FilePaths(BAD_FILE);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                Assert.False(fp.Initialized);
                _updateSink.Inits.ExpectNoValue();
                // The message text is the shared file loading code's (a behavior change in the PR
                // body); the level and the condition are unchanged.
                AssertLogMessageRegex(true, LogLevel.Error,
                    "^Unable to load flags: error parsing file: .*bad-file.txt\\]$");
            }
        }

        [Fact]
        public void DuplicateKeyAcrossFilesFailsTheLoadAndNamesTheKey()
        {
            using (var file1 = TempFile.Create())
            using (var file2 = TempFile.Create())
            {
                file1.SetContent(@"{""flagValues"":{""flag1"":""a""}}");
                file2.SetContent(@"{""flagValues"":{""flag1"":""b""}}");
                factory.FilePaths(file1.Path, file2.Path);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    Assert.False(fp.Initialized);
                    _updateSink.Inits.ExpectNoValue();
                    // The message text is the shared file loading code's (a behavior change in the
                    // PR body); the level and the condition are unchanged, and the key is still named.
                    AssertLogMessageRegex(true, LogLevel.Error,
                        "^Unable to load flags: flag \"flag1\" is specified by multiple files$");
                }
            }
        }

        [Fact]
        public void PathInAMissingDirectoryFailsTheLoadEvenWithSkipMissingPaths()
        {
            // SkipMissingPaths skips a file that does not exist in an existing directory. A path whose
            // directory does not exist is a failure.
            var missingDirectoryPath = "/nonexistent-ld-filedatasource-pin-dir/data.json";
            factory.FilePaths(ALL_DATA_JSON_FILE, missingDirectoryPath).SkipMissingPaths(true);
            using (var fp = MakeDataSource())
            {
                fp.Start();
                Assert.False(fp.Initialized);
                _updateSink.Inits.ExpectNoValue();
                // The message text is the shared file loading code's (behavior change D6 in the PR
                // body); the level and the condition are unchanged.
                AssertLogMessageRegex(true, LogLevel.Error,
                    "^Unable to load flags: unable to read file: .*" + missingDirectoryPath);
            }
        }

        [Fact]
        public void DeletingAWatchedFileKeepsTheStoreData()
        {
            using (var file = TempFile.Create())
            {
                factory.FilePaths(file.Path).AutoUpdate(true);
                file.SetContentFromPath(FLAG_ONLY_JSON_FILE);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    _updateSink.Inits.ExpectValue();

                    // A deletion is a change, and the reload it starts fails on the missing file. The
                    // data in the store is kept, because a load that fails never replaces it.
                    file.Delete();
                    _updateSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(500));
                }
            }
        }

        [Fact]
        public void FailedLoadAttemptsLogOneErrorThenDebugAndOneErrorWhenGivingUp()
        {
            // This replaces the pin of a warning per failed parse attempt (a behavior change in the
            // PR body): the first failure is logged once at Error, the identical failures of the
            // retries at Debug, and the end of the retry budget once at Error.
            // The path is in a directory that does not exist, so no change notification can start
            // a second run of attempts; the reader ignores the path anyway.
            var reader = new ScriptedFileReader();
            using (var dir = TempDirectory.Create())
            {
                factory.FilePaths(Path.Combine(dir.PathOf("no-such-directory"), "data.json"))
                    .AutoUpdate(true).FileReader(reader);
                using (var fp = MakeDataSource())
                {
                    fp.Start();
                    WaitUntil(() => LogCapture.HasMessageWithRegex(LogLevel.Error, "after 5 attempts"),
                        "the retry budget to end");
                    var messages = LogCapture.GetMessages();
                    Assert.Equal(1, messages.Count(m => m.Level == LogLevel.Error &&
                        m.Text.StartsWith("Unable to load flags: error parsing file")));
                    Assert.Equal(4, messages.Count(m => m.Level == LogLevel.Debug &&
                        m.Text.StartsWith("Unable to load flags: error parsing file")));
                    Assert.Equal(1, messages.Count(m => m.Level == LogLevel.Error &&
                        m.Text.Contains("attempts")));
                    Assert.Equal(5, reader.Reads);
                    Assert.False(fp.Initialized);
                }
            }
        }
    }
}
