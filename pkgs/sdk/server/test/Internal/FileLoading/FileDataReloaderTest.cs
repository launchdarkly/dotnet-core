using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using YamlDotNet.Serialization;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataReloaderTest : BaseTest, IDisposable
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
        // Short delays so the tests do not wait for the production ones. The bound stays at its
        // production value; the tests of the bound set their own.
        private static readonly FileDataReloaderTiming TestTiming = new FileDataReloaderTiming(
            TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20));

        private const string Flag1True = @"{""flagValues"": {""flag1"": true}}";
        private const string Flag1False = @"{""flagValues"": {""flag1"": false}}";
        private const string Truncated = @"{""flagValues""";

        private readonly TempDirectory _dir = TempDirectory.Create();
        private readonly string _path;
        private readonly EventSink<FileDataMergeResult> _applied = new EventSink<FileDataMergeResult>();
        private int _consumedErrorLogs;
        private int _applyCount;
        private FileDataReloader _reloader;

        public FileDataReloaderTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _path = _dir.PathOf("data.json");
        }

        public void Dispose()
        {
            _reloader?.Dispose();
            _dir.Dispose();
        }

        private FileDataReloaderConfig BasicReloaderConfig() =>
            new FileDataReloaderConfig
            {
                Paths = new[] { _path },
                DuplicateKeysHandling = FileDataDuplicateKeysHandling.Fail,
                Logger = TestLogger,
                FlagValueExpander = (key, value) => FileDataParser.MakeFallthroughFlagWithValue(key, value, 0),
                Apply = RecordApply
            };

        private void RecordApply(FileDataMergeResult result)
        {
            Interlocked.Increment(ref _applyCount);
            _applied.Enqueue(result);
        }

        private int ApplyCount => Volatile.Read(ref _applyCount);

        private FileDataReloader MakeReloader(Action<FileDataReloaderConfig> configure = null,
            FileDataReloaderTiming timing = null)
        {
            var config = BasicReloaderConfig();
            configure?.Invoke(config);
            _reloader = new FileDataReloader(config, timing ?? TestTiming);
            return _reloader;
        }

        private static FileDataReloaderTiming Timing(int debounceMs, int maxDebounceMs, int retryMs) =>
            new FileDataReloaderTiming(TimeSpan.FromMilliseconds(debounceMs), TimeSpan.FromMilliseconds(maxDebounceMs),
                TimeSpan.FromMilliseconds(retryMs));

        // Applications rewrite data files in place, which is not atomic. The tests do too, deliberately
        // slowly: a file that exists is truncated, and written 30 ms later, so a load can find it
        // empty, as it would with an application's writes, instead of only by chance.
        private void Write(string content)
        {
            if (!File.Exists(_path))
            {
                File.WriteAllText(_path, content);
                return;
            }
            InPlaceWrites.Rewrite(_path, content, TimeSpan.FromMilliseconds(30));
        }

        private FileDataMergeResult RequireApplied() => _applied.ExpectValue(TestTimeout);

        // The reloader reports a failure by logging it at error level once; identical repeats are
        // logged at debug level. These helpers observe the error-level entries in order.
        private List<string> ErrorLogs() =>
            LogCapture.GetMessages()
                .Where(m => m.Level == LogLevel.Error && m.Text.StartsWith("Unable to load flags: "))
                .Select(m => m.Text).ToList();

        private string RequireErrorLogged()
        {
            AssertEventually(() => ErrorLogs().Count > _consumedErrorLogs);
            return ErrorLogs()[_consumedErrorLogs++];
        }

        // An automatic retry can read the file while the test rewrites it in place (see Write), and
        // report that the file was empty. This skips such reports and returns the next other one.
        private string RequireErrorLoggedForContent()
        {
            AssertEventually(() => ErrorLogs().Skip(_consumedErrorLogs).Any(m => !m.Contains("no content")));
            var logs = ErrorLogs();
            var index = logs.FindIndex(_consumedErrorLogs, m => !m.Contains("no content"));
            _consumedErrorLogs = index + 1;
            return logs[index];
        }

        private void RequireQuiet(TimeSpan duration)
        {
            _applied.ExpectNoValue(duration);
            Assert.Equal(_consumedErrorLogs, ErrorLogs().Count);
        }

        // The test's own writes are not atomic (see Write). An automatic retry that runs while the
        // file is being rewritten reads it empty or half written and logs that failure. Those
        // entries are a correct response to what the retry saw, and tests that only check what
        // happens after the recovery discard them before they require silence.
        private void DrainErrors()
        {
            _consumedErrorLogs = ErrorLogs().Count;
        }

        private static string[] FlagKeys(FileDataMergeResult result) => result.Flags.Select(kv => kv.Key).ToArray();

        private int ErrorLogCount() => LogCapture.GetMessages().Count(m => m.Level == LogLevel.Error);

        private int GiveUpLogCount() =>
            LogCapture.GetMessages().Count(m => m.Level == LogLevel.Error && m.Text.Contains("attempts"));

        // The delays for the tests of a file that is found empty. The retry comes long enough after the
        // change signal for a test to finish rewriting the file before it, even on a slow machine.
        private static readonly FileDataReloaderTiming EmptyFileTiming = Timing(10, 1000, 1500);

        // Shorter than the retry delay of EmptyFileTiming, so a load that happens within it after a
        // change signal is not the retry.
        private static readonly TimeSpan BeforeTheRetry = TimeSpan.FromMilliseconds(1000);

        private const string ReadAgainLog = "reading it again";

        private const string YamlFlag1 = "flagValues:\n  flag1: true\n";
        private const string YamlFlag2 = "flagValues:\n  flag2: true\n";

        // Configures the reloader for the given files, with a YAML parser that returns null for a file
        // with no content.
        private FileDataReloader MakeYamlReloader(FileDataReloaderTiming timing, params string[] paths) =>
            MakeYamlReloader(timing, null, paths);

        private FileDataReloader MakeYamlReloader(FileDataReloaderTiming timing,
            Action<FileDataReloaderConfig> configure, params string[] paths)
        {
            var yaml = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
            return MakeReloader(c =>
            {
                configure?.Invoke(c);
                c.Paths = paths;
                c.ParsersByExtension = new Dictionary<string, Func<string, object>>
                {
                    [".yaml"] = content => yaml.Deserialize<object>(content)
                };
            }, timing);
        }

        [Fact]
        public void FileThatIsEmptiedIsReadAgainBeforeItIsApplied()
        {
            // A writer that rewrites the file in place truncates it and then writes the new content. A
            // load that finds the file empty after it had content keeps the data. In YAML an empty file
            // is valid, so applying it would briefly remove every entry.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, yamlPath);
            reloader.ReloadNow();
            Assert.Equal(new[] { "flag1" }, FlagKeys(RequireApplied()));

            File.WriteAllText(yamlPath, "");
            reloader.Trigger();
            RequireQuiet(TimeSpan.FromMilliseconds(200));
            AssertLogMessageRegex(true, LogLevel.Debug, ReadAgainLog);

            // The retry reads the rewritten file.
            File.WriteAllText(yamlPath, YamlFlag2);
            Assert.Equal(new[] { "flag2" }, FlagKeys(RequireApplied()));
            RequireQuiet(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void EmptyYamlFileThatStaysEmptyIsAppliedByTheRetry()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            reloader.Trigger();
            RequireQuiet(TimeSpan.FromMilliseconds(200));

            // The file is still empty when the retry reads it, so it is a document with no entries.
            Assert.Empty(RequireApplied().Flags);
        }

        [Fact]
        public void EmptyJsonFileFailsWhenItIsReadAgainAndKeepsTheData()
        {
            Write(Flag1True);
            var reloader = MakeReloader(timing: EmptyFileTiming);
            reloader.ReloadNow();
            RequireApplied();

            Write("");
            reloader.Trigger();

            // The load that the change signal starts reads the file again later instead of reporting
            // it. The retry finds it still empty, which is not a JSON document.
            RequireQuiet(TimeSpan.FromMilliseconds(200));
            Assert.Contains("no content", RequireErrorLogged());
            Assert.Equal(1, ApplyCount);
        }

        [Fact]
        public void FileThatStaysEmptyIsReadAgainOnlyOnce()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            reloader.Trigger();
            RequireQuiet(TimeSpan.FromMilliseconds(200));

            // A further change signal does not postpone the empty file again: it was empty when it
            // was last read, so this load applies it, before the retry would have.
            reloader.Trigger();
            Assert.Empty(_applied.ExpectValue(BeforeTheRetry).Flags);
        }

        [Fact]
        public void FileThatIsEmptyFromTheStartIsNotReadAgain()
        {
            // An empty YAML file next to the data, such as a placeholder for later entries, has never
            // had content. It does not hold back the changes of the other file.
            var placeholderPath = _dir.PathOf("placeholder.yaml");
            File.WriteAllText(_path, Flag1True);
            File.WriteAllText(placeholderPath, "");
            var reloader = MakeYamlReloader(EmptyFileTiming, _path, placeholderPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(_path, Flag1False);
            reloader.Trigger();
            var flag = Assert.IsType<FeatureFlag>(_applied.ExpectValue(BeforeTheRetry).Flags[0].Value.Item);
            Assert.Equal(new[] { LdValue.Of(false) }, flag.Variations);
            AssertLogMessageRegex(false, LogLevel.Debug, ReadAgainLog);
        }

        [Fact]
        public void RetryThatFindsAFileEmptiedReadsItAgain()
        {
            // A retry left over from a failure can run while the file is being rewritten in place. It
            // reads the file again too, rather than applying it empty.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, yamlPath);
            reloader.ReloadNow();
            RequireApplied();
            File.WriteAllText(yamlPath, "flagValues: [");
            reloader.Trigger();
            RequireErrorLogged();

            // No change signal: only the retry reads the file.
            File.WriteAllText(yamlPath, "");
            AssertEventually(() => LogCapture.HasMessageWithRegex(LogLevel.Debug, ReadAgainLog));
            File.WriteAllText(yamlPath, YamlFlag2);
            Assert.Equal(new[] { "flag2" }, FlagKeys(RequireApplied()));
        }

        [Fact]
        public void FileThatIsDeletedAndCreatedEmptyIsNotReadAgain()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, c => c.SkipMissingPaths = true, yamlPath);
            reloader.ReloadNow();
            RequireApplied();
            File.Delete(yamlPath);
            reloader.Trigger();
            Assert.Empty(RequireApplied().Flags);

            // A missing file had no content, so the empty file that replaces it is applied right away.
            File.WriteAllText(yamlPath, "");
            reloader.Trigger();
            Assert.Empty(_applied.ExpectValue(BeforeTheRetry).Flags);
        }

        [Fact]
        public void RetryLeftOverFromAReadAgainDoesNotLoadAfterASuccessfulReloadNow()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, yamlPath);
            reloader.ReloadNow();
            RequireApplied();
            File.WriteAllText(yamlPath, "");
            reloader.Trigger();
            RequireQuiet(TimeSpan.FromMilliseconds(200));

            // A load on demand reads the rewritten file before the retry does.
            File.WriteAllText(yamlPath, YamlFlag2);
            reloader.ReloadNow();
            Assert.Equal(new[] { "flag2" }, FlagKeys(RequireApplied()));

            // The retry can still fire, but nothing needs to be read again, so it does not load. A
            // changed file with no trigger must not be picked up.
            File.WriteAllText(yamlPath, YamlFlag1);
            RequireQuiet(EmptyFileTiming.RetryDelay + TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void EmptyFileIsNotReadAgainWhenRetryIsOff()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, c => c.RetryOnFailure = false, yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            // Without a retry, nothing would read the file again, so the load applies it as it is.
            File.WriteAllText(yamlPath, "");
            reloader.ReloadNow();
            Assert.Empty(_applied.ExpectValue(TimeSpan.Zero).Flags);
        }

        [Fact]
        public void ReadingAFileAgainDoesNotUseUpALoadAttempt()
        {
            Write(Flag1True);
            var reader = new CountingFileReader();
            var reloader = MakeReloader(c =>
            {
                c.FileReader = reader;
                c.MaxLoadAttempts = 2;
            });
            reloader.ReloadNow();
            RequireApplied();

            Write("");
            reloader.Trigger();

            // After the initial load: the read again, then the two attempts of the budget.
            AssertEventually(() => LogCapture.HasMessageWithRegex(LogLevel.Error, "after 2 attempts"));
            _applied.ExpectNoValue(TimeSpan.FromMilliseconds(100));
            Assert.Equal(4, reader.Reads);
        }

        [Fact]
        public void EmptyYamlFileAtTheInitialLoadIsADocumentWithNoEntries()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, "");
            var reloader = MakeYamlReloader(EmptyFileTiming, yamlPath);

            reloader.ReloadNow();

            // The initial load applies what it reads, without waiting for a retry.
            Assert.Empty(_applied.ExpectValue(TimeSpan.Zero).Flags);
        }

        [Fact]
        public void EmptyJsonFileAtTheInitialLoadFailsToParse()
        {
            Write("");
            var reloader = MakeReloader(timing: EmptyFileTiming);

            reloader.ReloadNow();

            Assert.Contains("no content", RequireErrorLogged());
            Assert.Equal(0, ApplyCount);
        }

        [Fact]
        public void FailsOnMissingPathByDefault()
        {
            Write(Flag1True);
            var missing = _dir.PathOf("missing.json");
            var reloader = MakeReloader(c => c.Paths = new[] { _path, missing });

            reloader.ReloadNow();

            var message = RequireErrorLogged();
            Assert.Contains("unable to read file", message);
            Assert.Contains(missing, message);
            RequireQuiet(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void SkipsMissingPathsWhenConfigured()
        {
            Write(Flag1True);
            var second = _dir.PathOf("second.json");
            var reloader = MakeReloader(c =>
            {
                c.Paths = new[] { _path, second };
                c.SkipMissingPaths = true;
                c.SkipUnchanged = true;
            });

            // Step 1: one file exists and one does not. The reload succeeds with the existing file.
            reloader.ReloadNow();
            var result = RequireApplied();
            Assert.Equal(new[] { "flag1" }, FlagKeys(result));
            Assert.Equal(2, result.Files.Count);
            Assert.Equal(_path, result.Files[0].Path);
            Assert.True(result.Files[0].Present);
            Assert.Equal(1, result.Files[0].Flags);
            Assert.Equal(0, result.Files[0].Segments);
            Assert.Equal(second, result.Files[1].Path);
            Assert.False(result.Files[1].Present);
            Assert.Equal(0, result.Files[1].Flags);

            // Step 2: the missing file appears. Its data is merged in.
            File.WriteAllText(second, @"{""flagValues"": {""flag2"": true}}");
            reloader.ReloadNow();
            result = RequireApplied();
            Assert.Equal(new[] { "flag1", "flag2" }, FlagKeys(result));
            Assert.True(result.Files[1].Present);
            Assert.Equal(1, result.Files[1].Flags);

            // Step 3: the file is deleted. Its data is gone and the reload still succeeds.
            File.Delete(second);
            reloader.ReloadNow();
            result = RequireApplied();
            Assert.Equal(new[] { "flag1" }, FlagKeys(result));
            RequireQuiet(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void MissingDirectoryIsAlsoSkippedWhenConfigured()
        {
            Write(Flag1True);
            var inMissingDirectory = Path.Combine(_dir.PathOf("no-such-directory"), "data.json");
            var reloader = MakeReloader(c =>
            {
                c.Paths = new[] { _path, inMissingDirectory };
                c.SkipMissingPaths = true;
            });

            reloader.ReloadNow();

            var result = RequireApplied();
            Assert.Equal(new[] { "flag1" }, FlagKeys(result));
            Assert.False(result.Files[1].Present);
        }

        [Fact]
        public void InitialLoadAppliesTheData()
        {
            Write(Flag1True);
            var reloader = MakeReloader();

            reloader.ReloadNow();

            var result = RequireApplied();
            Assert.Equal(new[] { "flag1" }, FlagKeys(result));
            var flag = Assert.IsType<FeatureFlag>(result.Flags[0].Value.Item);
            Assert.Equal(new[] { LdValue.Of(true) }, flag.Variations);
            Assert.Single(result.Files);
            Assert.True(result.Files[0].Present);
        }

        [Fact]
        public void ReportsParseFailureAndAppliesNothing()
        {
            Write(Truncated);
            var reloader = MakeReloader();

            reloader.ReloadNow();

            var message = RequireErrorLogged();
            Assert.StartsWith("Unable to load flags: error parsing file", message);
            Assert.Contains(_path, message);
            RequireQuiet(TimeSpan.FromMilliseconds(50));
        }

        [Fact]
        public void ReportsMergeFailure()
        {
            Write(Flag1True);
            var second = _dir.PathOf("second.json");
            File.WriteAllText(second, Flag1False);
            var reloader = MakeReloader(c => c.Paths = new[] { _path, second });

            reloader.ReloadNow();

            Assert.Equal("Unable to load flags: flag \"flag1\" is specified by multiple files", RequireErrorLogged());
            _applied.ExpectNoValue(TimeSpan.FromMilliseconds(50));
        }

        [Fact]
        public void ReadFailureIsReported()
        {
            Write(Flag1True);
            var reader = new ThrowingFileReader();
            var reloader = MakeReloader(c => c.FileReader = reader);

            reloader.ReloadNow();

            var message = RequireErrorLogged();
            Assert.Contains("unable to read file: simulated read error", message);
            Assert.Contains(_path, message);
        }

        [Fact]
        public void DebounceCoalescesTriggers()
        {
            // The settle window is much longer than the whole trigger burst, so that a scheduling
            // stall during the burst cannot let the debounce fire early and split the reloads.
            // SkipUnchanged stays off so that every reload is observable as an Apply.
            Write(Flag1True);
            var reloader = MakeReloader(timing: Timing(400, 1000, 20));

            Write(Flag1False);
            for (var i = 0; i < 20; i++)
            {
                reloader.Trigger();
                Thread.Sleep(1);
            }

            RequireApplied();
            // The burst must coalesce into one reload, with a tolerance of one more: a debounce
            // tick racing a fresh trigger can produce a single extra serialized reload.
            Thread.Sleep(600);
            var extraApplies = ApplyCount - 1;
            Assert.True(extraApplies <= 1, "trigger burst was not coalesced: " + extraApplies + " extra applies");
        }

        [Fact]
        public void DebounceWindowIsExtendedByEachTrigger()
        {
            // The debounce is a settle window: each trigger moves the deadline out again. A stream
            // of notifications spaced closer together than the window must produce no reload while
            // the stream continues, and exactly one reload after it stops.
            // The notifications are spaced far inside the window so that a scheduling stall on a
            // busy machine cannot let the window expire between two of them.
            // The bound on postponing is set far beyond the stream here, because this test is about
            // the window alone; the bound has its own test below.
            Write(Flag1True);
            var window = TimeSpan.FromMilliseconds(600);
            var reloader = MakeReloader(timing: Timing(600, 60000, 20));

            var stop = DateTime.UtcNow + TimeSpan.FromTicks(window.Ticks * 3);
            while (DateTime.UtcNow < stop)
            {
                reloader.Trigger();
                Thread.Sleep(50);
            }
            Assert.True(ApplyCount == 0, "a reload ran while change notifications were still arriving");

            RequireApplied();
            RequireQuiet(TimeSpan.FromTicks(window.Ticks * 2));
            Assert.Equal(1, ApplyCount);
        }

        [Fact]
        public void DebounceCannotBePostponedBeyondTheMaximum()
        {
            // A stream of notifications that never settles, for example a file that is written
            // continuously, must still reload, no later than the maximum after the first
            // notification of the stream. The notifications are spaced far inside the settle
            // window, so only the bound can let a reload run while the stream continues.
            Write(Flag1True);
            var reloader = MakeReloader(timing: Timing(200, 400, 20));

            var stop = DateTime.UtcNow + TimeSpan.FromMilliseconds(3000);
            while (DateTime.UtcNow < stop && ApplyCount == 0)
            {
                reloader.Trigger();
                Thread.Sleep(50);
            }
            Assert.True(ApplyCount >= 1, "no reload ran while change notifications kept arriving");
        }

        [Fact]
        public void TriggerReloadsOnAWorkerThread()
        {
            Write(Flag1True);
            var applyThread = -1;
            var reloader = MakeReloader(c =>
            {
                c.Apply = result =>
                {
                    applyThread = Thread.CurrentThread.ManagedThreadId;
                    _applied.Enqueue(result);
                };
            });

            reloader.Trigger();

            RequireApplied();
            Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, applyThread);
        }

        [Fact]
        public void ReportsIdenticalFailureOnlyOnce()
        {
            Write(Truncated);
            var reloader = MakeReloader();

            reloader.ReloadNow();
            var first = RequireErrorLogged();

            // The automatic retries keep failing identically. The error is not logged at error
            // level again.
            RequireQuiet(TimeSpan.FromMilliseconds(200));
            Assert.Equal(1, ErrorLogCount());

            // A different failure is a new report.
            Write(@"{""flagValues"": {bad}}");
            var second = RequireErrorLoggedForContent();
            Assert.Contains("error parsing file", second);
            Assert.NotEqual(first, second);

            // Success re-arms reporting: the same failure recurring afterward is reported again.
            Write(Flag1True);
            RequireApplied();
            DrainErrors();
            Write(Truncated);
            reloader.Trigger();
            Assert.Equal(first, RequireErrorLogged());
        }

        [Fact]
        public void RetriesAfterFailureWithoutFurtherTriggers()
        {
            Write(Flag1True);
            var reloader = MakeReloader();
            reloader.ReloadNow();
            RequireApplied();

            Write(Truncated);
            reloader.Trigger();
            RequireErrorLogged();

            // Fix the file without triggering. Only the automatic retry can observe the fix.
            Write(Flag1False);
            var result = RequireApplied();
            var flag = Assert.IsType<FeatureFlag>(result.Flags[0].Value.Item);
            Assert.Equal(new[] { LdValue.Of(false) }, flag.Variations);
        }

        [Fact]
        public void InitialLoadFailureArmsTheRetry()
        {
            Write(Truncated);
            var reloader = MakeReloader();

            reloader.ReloadNow();
            RequireErrorLogged();

            Write(Flag1True);
            RequireApplied();
        }

        [Fact]
        public void StopsRetryingAfterSuccess()
        {
            Write(Truncated);
            var reloader = MakeReloader(c => c.SkipUnchanged = true);
            reloader.ReloadNow();
            RequireErrorLogged();

            Write(Flag1True);
            RequireApplied();
            DrainErrors();

            // After the successful reload there are no further attempts. A changed file with no
            // trigger must not be picked up.
            Write(Flag1False);
            RequireQuiet(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void RetryLeftOverAfterASuccessfulReloadNowDoesNotLoad()
        {
            Write(Truncated);
            var reloader = MakeReloader(c => c.SkipUnchanged = true);
            reloader.ReloadNow();
            RequireErrorLogged();

            // The file is fixed and loaded on demand. Whether the retry or this load applies the
            // recovery, the content is applied once.
            Write(Flag1True);
            reloader.ReloadNow();
            RequireApplied();
            DrainErrors();

            // The retry armed by the failure can still fire after the recovery, but nothing has
            // failed since, so it does not load. A changed file with no trigger must not be picked
            // up.
            Write(Flag1False);
            RequireQuiet(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void NoRetryWhenRetryIsOff()
        {
            Write(Truncated);
            var reloader = MakeReloader(c => c.RetryOnFailure = false);
            reloader.ReloadNow();
            RequireErrorLogged();

            Write(Flag1True);
            RequireQuiet(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void SkipUnchangedSuppressesIdenticalContent()
        {
            Write(Flag1True);
            var reloader = MakeReloader(c => c.SkipUnchanged = true);
            reloader.ReloadNow();
            RequireApplied();

            reloader.Trigger();
            RequireQuiet(TimeSpan.FromMilliseconds(100));

            Write(Flag1False);
            reloader.Trigger();
            RequireApplied();
        }

        [Fact]
        public void RecoveryAppliesEvenWhenContentIsUnchanged()
        {
            Write(Flag1True);
            var reloader = MakeReloader(c => c.SkipUnchanged = true);
            reloader.ReloadNow();
            RequireApplied();

            // A reload fails and is logged.
            File.Delete(_path);
            reloader.Trigger();
            RequireErrorLogged();

            // The file comes back with identical content. The success must be applied despite
            // SkipUnchanged, because only Apply shows the consumer that the failure is over.
            Write(Flag1True);
            reloader.Trigger();
            RequireApplied();

            // Once recovered, identical content skips again.
            reloader.Trigger();
            RequireQuiet(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void SkipUnchangedDistinguishesWhichFileHoldsTheContent()
        {
            // The same bytes moving from one configured file to another is a change: the merged
            // data is the same, but the report of what each file supplied is not.
            var second = _dir.PathOf("second.json");
            Write(Flag1True);
            var reloader = MakeReloader(c =>
            {
                c.Paths = new[] { _path, second };
                c.SkipMissingPaths = true;
                c.SkipUnchanged = true;
            });
            reloader.ReloadNow();
            var result = RequireApplied();
            Assert.True(result.Files[0].Present);
            Assert.False(result.Files[1].Present);

            File.Delete(_path);
            File.WriteAllText(second, Flag1True);
            reloader.ReloadNow();
            result = RequireApplied();
            Assert.False(result.Files[0].Present);
            Assert.True(result.Files[1].Present);
        }

        [Fact]
        public void ProductionTimingIsASettleWindowOfOneTenthOfASecondBoundedAtOneSecondAndARetryAfterOneSecond()
        {
            var timing = FileDataReloaderTiming.Production;
            Assert.Equal(TimeSpan.FromMilliseconds(100), timing.DebounceDelay);
            Assert.Equal(TimeSpan.FromSeconds(1), timing.MaxDebounceDelay);
            Assert.Equal(TimeSpan.FromSeconds(1), timing.RetryDelay);
        }

        [Theory]
        [InlineData(0, 1000, 20)]
        [InlineData(10, 0, 20)]
        [InlineData(10, 1000, 0)]
        [InlineData(-10, 1000, 20)]
        public void TimingRejectsADelayThatIsNotPositive(int debounceMs, int maxDebounceMs, int retryMs)
        {
            Assert.Throws<ArgumentException>(() => Timing(debounceMs, maxDebounceMs, retryMs));
        }

        [Fact]
        public void TimingRejectsASettleWindowLongerThanItsBound()
        {
            Assert.Throws<ArgumentException>(() => Timing(600, 400, 20));
            Timing(400, 400, 20);
        }

        [Fact]
        public void AppliesEveryReloadWhenSkipUnchangedIsOff()
        {
            Write(Flag1True);
            var reloader = MakeReloader();
            reloader.ReloadNow();
            RequireApplied();
            reloader.Trigger();
            RequireApplied();
        }

        [Fact]
        public void MergesMultipleFilesInOrder()
        {
            var first = _dir.PathOf("first.json");
            var second = _dir.PathOf("second.json");
            File.WriteAllText(first, @"{""flags"": {""flag1"": {""key"": ""flag1"", ""version"": 1}}}");
            File.WriteAllText(second, @"{""flags"": {""flag1"": {""key"": ""flag1"", ""version"": 2}}}");
            var reloader = MakeReloader(c =>
            {
                c.Paths = new[] { first, second };
                c.DuplicateKeysHandling = FileDataDuplicateKeysHandling.Ignore;
            });

            reloader.ReloadNow();

            var result = RequireApplied();
            Assert.Single(result.Flags);
            Assert.Equal(1, result.Flags[0].Value.Version);
            Assert.Equal(2, result.Files.Count);
            Assert.Equal(1, result.Files[0].Flags);
            Assert.Equal(0, result.Files[1].Flags);
        }

        [Fact]
        public void DoesNothingAfterDispose()
        {
            Write(Flag1True);
            var reloader = MakeReloader();
            reloader.ReloadNow();
            RequireApplied();

            reloader.Dispose();
            reloader.Dispose();
            reloader.Trigger();
            reloader.ReloadNow();
            RequireQuiet(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void PendingRetryIsCanceledByDispose()
        {
            Write(Truncated);
            var reloader = MakeReloader();
            reloader.ReloadNow();
            RequireErrorLogged();

            reloader.Dispose();
            Write(Flag1True);
            RequireQuiet(TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public void DisposeDoesNotWaitForAnInFlightReload()
        {
            Write(Flag1True);
            var applyEntered = new ManualResetEventSlim();
            var applyRelease = new ManualResetEventSlim();
            var reloader = MakeReloader(c =>
            {
                c.Apply = result =>
                {
                    applyEntered.Set();
                    applyRelease.Wait();
                    _applied.Enqueue(result);
                };
            });

            try
            {
                reloader.Trigger();
                Assert.True(applyEntered.Wait(TestTimeout), "timed out waiting for the reload to start");

                // The reload is parked inside Apply. Dispose must return anyway, because a reload wedged
                // in blocking I/O must not be able to wedge shutdown.
                var disposed = Task.Run(() => reloader.Dispose());
                Assert.True(disposed.Wait(TestTimeout), "Dispose blocked on an in-flight reload");
            }
            finally
            {
                // The parked reload must always be released, or a failing assertion leaves a thread
                // blocked forever.
                applyRelease.Set();
            }
            RequireApplied();
        }

        [Fact]
        public void ReloadsAreSerialized()
        {
            Write(Flag1True);
            var concurrent = 0;
            var maxConcurrent = 0;
            var reloader = MakeReloader(c =>
            {
                c.Apply = result =>
                {
                    var now = Interlocked.Increment(ref concurrent);
                    InterlockedMax(ref maxConcurrent, now);
                    Thread.Sleep(20);
                    Interlocked.Decrement(ref concurrent);
                    _applied.Enqueue(result);
                };
            });

            var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
            {
                reloader.ReloadNow();
                reloader.Trigger();
            })).ToArray();
            Task.WaitAll(tasks);
            for (var i = 0; i < 8; i++)
            {
                RequireApplied();
            }

            Assert.Equal(1, maxConcurrent);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value)
            {
                if (Interlocked.CompareExchange(ref target, value, current) == current)
                {
                    return;
                }
            }
        }

        [Fact]
        public void ApplyExceptionIsReportedAndRetried()
        {
            Write(Flag1True);
            var failNext = true;
            var reloader = MakeReloader(c =>
            {
                c.SkipUnchanged = true;
                c.Apply = result =>
                {
                    if (failNext)
                    {
                        failNext = false;
                        throw new InvalidOperationException("consumer failed");
                    }
                    _applied.Enqueue(result);
                };
            });

            // The consumer rejects the result. That is a load failure like any other.
            reloader.Trigger();
            Assert.Contains("Unable to load flags: error applying file data: consumer failed", RequireErrorLogged());

            // The rejected result was not remembered as applied: the retry offers the unchanged content
            // again, and this time the consumer accepts it.
            RequireApplied();
        }

        [Fact]
        public void FlagValueExpanderExceptionIsReportedAsAFailure()
        {
            Write(Flag1True);
            var failNext = true;
            var reloader = MakeReloader(c =>
            {
                c.FlagValueExpander = (key, value) =>
                {
                    if (failNext)
                    {
                        failNext = false;
                        throw new InvalidOperationException("expander failed");
                    }
                    return FileDataParser.MakeFallthroughFlagWithValue(key, value, 0);
                };
            });

            reloader.ReloadNow();

            Assert.Contains("expander failed", RequireErrorLogged());
            _applied.ExpectNoValue(TimeSpan.Zero);

            // The failure armed the retry like a read failure.
            RequireApplied();
        }

        [Fact]
        public void TriggersDuringARunningReloadCoalesceIntoOneFollowUpReload()
        {
            var reader = new BlockingFileReader();
            var reloader = MakeReloader(c => c.FileReader = reader, Timing(5, 1000, 20));
            try
            {
                reloader.Trigger();
                Assert.True(reader.Entered.Wait(TestTimeout), "timed out waiting for the first reload to start");

                // More change signals arrive while the first reload is blocked in its read. Each one is
                // far enough apart for the settle window to expire between them.
                for (var i = 0; i < 10; i++)
                {
                    reloader.Trigger();
                    Thread.Sleep(30);
                }
            }
            finally
            {
                reader.Release.Set();
            }

            // The signals all describe the same state of the files, so one follow-up reload serves
            // them all: two reloads in total, and no more after that.
            RequireApplied();
            RequireApplied();
            RequireQuiet(TimeSpan.FromMilliseconds(100));
            Assert.Equal(2, reader.Reads);
        }

        [Fact]
        public void GivesUpAfterMaxLoadAttempts()
        {
            Write(Truncated);
            var reader = new CountingFileReader();
            var reloader = MakeReloader(c =>
            {
                c.FileReader = reader;
                c.MaxLoadAttempts = 3;
            });

            reloader.ReloadNow();
            RequireErrorLogged();

            // The initial attempt and two retries fail, and the end of the budget is logged once.
            // Then nothing runs: a fix without a trigger is not picked up.
            AssertEventually(() => LogCapture.HasMessageWithRegex(LogLevel.Error, "after 3 attempts"));
            Write(Flag1True);
            RequireQuiet(TimeSpan.FromMilliseconds(200));
            Assert.Equal(3, reader.Reads);
            Assert.Equal(1, GiveUpLogCount());
        }

        [Fact]
        public void TriggerAfterGivingUpStartsOverWithAFreshBudget()
        {
            Write(Truncated);
            var reader = new CountingFileReader();
            var reloader = MakeReloader(c =>
            {
                c.FileReader = reader;
                c.MaxLoadAttempts = 3;
            });
            reloader.ReloadNow();
            RequireErrorLogged();
            AssertEventually(() => LogCapture.HasMessageWithRegex(LogLevel.Error, "after 3 attempts"));

            // The file is still bad. A change signal is a new run of attempts: three more, not
            // one, before the reloader gives up again. The new run reports the failure again.
            reloader.Trigger();
            RequireErrorLogged();
            AssertEventually(() => reader.Reads >= 6);
            RequireQuiet(TimeSpan.FromMilliseconds(200));
            Assert.Equal(6, reader.Reads);
            Assert.Equal(2, GiveUpLogCount());

            // A signal after the file is fixed loads it.
            Write(Flag1True);
            reloader.Trigger();
            RequireApplied();
        }

        [Fact]
        public void RetriesWithoutBoundByDefault()
        {
            Write(Truncated);
            var reader = new CountingFileReader();
            var reloader = MakeReloader(c => c.FileReader = reader);
            reloader.ReloadNow();
            RequireErrorLogged();

            // Far more attempts than any bound a consumer would set, and no giving up.
            AssertEventually(() => reader.Reads >= 20);
            AssertLogMessageRegex(false, LogLevel.Error, "attempts");

            Write(Flag1True);
            RequireApplied();
        }

        [Fact]
        public void NoGiveUpWithoutRetries()
        {
            Write(Truncated);
            var reloader = MakeReloader(c =>
            {
                c.RetryOnFailure = false;
                c.MaxLoadAttempts = 1;
            });
            reloader.ReloadNow();
            RequireErrorLogged();
            reloader.ReloadNow();
            RequireErrorLogged();

            // Without retries there is nothing to give up: each load is one attempt, reported as a
            // failure, and no line announces the end of a retry budget.
            AssertLogMessageRegex(false, LogLevel.Error, "attempts");
        }

        [Fact]
        public void GiveUpLineNamesTheFailure()
        {
            Write(Truncated);
            var reloader = MakeReloader(c => c.MaxLoadAttempts = 2);
            reloader.ReloadNow();
            var reason = RequireErrorLogged().Substring("Unable to load flags: ".Length);

            // The attempts before the end of the budget may have repeated an earlier failure at
            // debug level only, so the line that ends it says why.
            AssertEventually(() => LogCapture.HasMessageWithRegex(LogLevel.Error, "after 2 attempts"));
            Assert.Contains(LogCapture.GetMessages(), m => m.Level == LogLevel.Error &&
                m.Text.Contains("after 2 attempts") && m.Text.EndsWith(reason));
        }

        [Fact]
        public void FailureThatRepeatsOnANewReloadIsReportedAgain()
        {
            Write(Truncated);
            var reloader = MakeReloader();
            reloader.ReloadNow();
            var first = RequireErrorLogged();

            // The automatic retries repeat the failure at debug level only.
            RequireQuiet(TimeSpan.FromMilliseconds(100));
            Assert.Equal(1, ErrorLogCount());

            // A reload that is not a retry is a new event: the same failure is reported and logged
            // at error level again, with its reason.
            reloader.ReloadNow();
            var second = RequireErrorLogged();
            Assert.Equal(first, second);
            Assert.Equal(2, LogCapture.GetMessages().Count(m => m.Level == LogLevel.Error &&
                m.Text.Contains("error parsing file")));
        }

        [Fact]
        public void RetryThatFiresDuringASuccessfulReloadDoesNotApplyAgain()
        {
            Write(Truncated);
            var reader = new BlockingSecondReadFileReader();
            var reloader = MakeReloader(c => c.FileReader = reader, Timing(10, 1000, 300));
            reloader.ReloadNow();
            RequireErrorLogged(); // a retry is due in 300 ms

            // The next synchronous reload blocks in its read past the retry deadline. The retry
            // fires and waits for the reload lock while that reload succeeds.
            Write(Flag1True);
            var second = Task.Run(() => reloader.ReloadNow());
            try
            {
                Assert.True(reader.Entered.Wait(TestTimeout), "timed out waiting for the second read");
                Thread.Sleep(600); // twice the retry delay: the retry has fired by now
            }
            finally
            {
                reader.Release.Set();
            }
            Assert.True(second.Wait(TestTimeout), "the second reload did not finish");

            // The stale retry reads and applies nothing: nothing has failed since the success.
            RequireApplied();
            RequireQuiet(TimeSpan.FromMilliseconds(200));
            Assert.Equal(1, ApplyCount);
            Assert.Equal(2, reader.Reads);
        }

        [Fact]
        public void MissingDirectoryFailsWhenFailOnMissingDirectoryIsSet()
        {
            Write(Flag1True);
            var missingFile = _dir.PathOf("missing.json");
            var inMissingDirectory = Path.Combine(_dir.PathOf("no-such-directory"), "data.json");
            var reloader = MakeReloader(c =>
            {
                c.Paths = new[] { _path, missingFile, inMissingDirectory };
                c.SkipMissingPaths = true;
                c.FailOnMissingDirectory = true;
            });

            reloader.ReloadNow();

            // The missing file is still skipped; the path in the missing directory fails the load.
            var message = RequireErrorLogged();
            Assert.Contains(inMissingDirectory, message);
            _applied.ExpectNoValue(TimeSpan.Zero);
        }

        // Reads the file as the default reader does, and counts the reads, so a test can observe
        // how many load attempts were made.
        private class CountingFileReader : FileDataTypes.IFileReader
        {
            private int _reads;

            public int Reads => Volatile.Read(ref _reads);

            public string ReadAllText(string path)
            {
                Interlocked.Increment(ref _reads);
                return FileDataReader.Instance.ReadAllText(path);
            }
        }

        // Reads the file as the default reader does, but holds the second read until released, so
        // a reload can be held in progress after an earlier one has failed.
        private class BlockingSecondReadFileReader : FileDataTypes.IFileReader
        {
            public readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            private int _reads;

            public int Reads => Volatile.Read(ref _reads);

            public string ReadAllText(string path)
            {
                if (Interlocked.Increment(ref _reads) == 2)
                {
                    Entered.Set();
                    Release.Wait();
                }
                return FileDataReader.Instance.ReadAllText(path);
            }
        }

        // Blocks the first read until released, so a reload can be held in progress.
        private class BlockingFileReader : FileDataTypes.IFileReader
        {
            public readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            private int _reads;

            public int Reads => Volatile.Read(ref _reads);

            public string ReadAllText(string path)
            {
                if (Interlocked.Increment(ref _reads) == 1)
                {
                    Entered.Set();
                    Release.Wait();
                }
                return "{}";
            }
        }

        private static void AssertEventually(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (!condition() && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
            Assert.True(condition());
        }

        private class ThrowingFileReader : FileDataTypes.IFileReader
        {
            public string ReadAllText(string path) => throw new IOException("simulated read error");
        }
    }
}
