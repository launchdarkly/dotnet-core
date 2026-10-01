using System;
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

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataReloaderTest : BaseTest, IDisposable
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

        private const string Flag1True = @"{""flagValues"": {""flag1"": true}}";
        private const string Flag1False = @"{""flagValues"": {""flag1"": false}}";
        private const string Truncated = @"{""flagValues""";

        private readonly TempDirectory _dir = TempDirectory.Create();
        private readonly string _path;
        private readonly EventSink<FileDataMergeResult> _applied = new EventSink<FileDataMergeResult>();
        private readonly EventSink<Exception> _errored = new EventSink<Exception>();
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
                Apply = RecordApply,
                OnError = e => _errored.Enqueue(e)
            };

        private void RecordApply(FileDataMergeResult result)
        {
            Interlocked.Increment(ref _applyCount);
            _applied.Enqueue(result);
        }

        private int ApplyCount => Volatile.Read(ref _applyCount);

        private FileDataReloader MakeReloader(Action<FileDataReloaderConfig> configure = null)
        {
            var config = BasicReloaderConfig();
            configure?.Invoke(config);
            _reloader = new FileDataReloader(config);
            return _reloader;
        }

        private void Write(string content) => File.WriteAllText(_path, content);

        private FileDataMergeResult RequireApplied() => _applied.ExpectValue(TestTimeout);

        private Exception RequireErrored() => _errored.ExpectValue(TestTimeout);

        private void RequireQuiet(TimeSpan duration)
        {
            _applied.ExpectNoValue(duration);
            _errored.ExpectNoValue(TimeSpan.Zero);
        }

        private static string[] FlagKeys(FileDataMergeResult result) => result.Flags.Select(kv => kv.Key).ToArray();

        private int ErrorLogCount() => LogCapture.GetMessages().Count(m => m.Level == LogLevel.Error);

        [Fact]
        public void FailsOnMissingPathByDefault()
        {
            Write(Flag1True);
            var missing = _dir.PathOf("missing.json");
            var reloader = MakeReloader(c => c.Paths = new[] { _path, missing });

            reloader.ReloadNow();

            var e = Assert.IsType<FileDataReadException>(RequireErrored());
            Assert.Equal(missing, e.Path);
            Assert.Contains("unable to read file", e.Message);
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

            var e = Assert.IsType<FileDataReadException>(RequireErrored());
            Assert.Equal(_path, e.Path);
            Assert.Contains("error parsing file", e.Message);
            AssertLogMessageRegex(true, LogLevel.Error, "Unable to load flags: error parsing file");
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

            var e = RequireErrored();
            Assert.IsType<FileDataException>(e);
            Assert.Equal("flag \"flag1\" is specified by multiple files", e.Message);
            _applied.ExpectNoValue(TimeSpan.FromMilliseconds(50));
        }

        [Fact]
        public void ReadFailureIsReportedAsAReadException()
        {
            Write(Flag1True);
            var reader = new ThrowingFileReader();
            var reloader = MakeReloader(c => c.FileReader = reader);

            reloader.ReloadNow();

            var e = Assert.IsType<FileDataReadException>(RequireErrored());
            Assert.Equal(_path, e.Path);
            Assert.Contains("unable to read file", e.Message);
            Assert.IsType<IOException>(e.InnerException);
        }

        [Fact]
        public void DebounceCoalescesTriggers()
        {
            // The settle window is much longer than the whole trigger burst, so that a scheduling
            // stall during the burst cannot let the debounce fire early and split the reloads.
            // SkipUnchanged stays off so that every reload is observable as an Apply.
            Write(Flag1True);
            var reloader = MakeReloader(c => c.DebounceDelay = TimeSpan.FromMilliseconds(400));

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
            Write(Flag1True);
            var window = TimeSpan.FromMilliseconds(600);
            var reloader = MakeReloader(c => c.DebounceDelay = window);

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
        public void TriggerWithoutDebounceReloadsOnAWorkerThread()
        {
            Write(Flag1True);
            var applyThread = -1;
            var reloader = MakeReloader(c =>
            {
                c.DebounceDelay = TimeSpan.Zero;
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
            var reloader = MakeReloader(c => c.RetryDelay = TimeSpan.FromMilliseconds(10));

            reloader.ReloadNow();
            RequireErrored();

            // The automatic retries keep failing identically. The error is neither reported to
            // OnError again nor logged at error level again.
            RequireQuiet(TimeSpan.FromMilliseconds(200));
            Assert.Equal(1, ErrorLogCount());

            // A different failure is a new report.
            Write(@"{""flagValues"": {bad}}");
            var e = RequireErrored();
            Assert.Contains("error parsing file", e.Message);

            // Success re-arms reporting: the same failure recurring afterward is reported again.
            Write(Flag1True);
            RequireApplied();
            Write(Truncated);
            reloader.Trigger();
            RequireErrored();
        }

        [Fact]
        public void RetriesAfterFailureWithoutFurtherTriggers()
        {
            Write(Flag1True);
            var reloader = MakeReloader(c => c.RetryDelay = TimeSpan.FromMilliseconds(20));
            reloader.ReloadNow();
            RequireApplied();

            Write(Truncated);
            reloader.Trigger();
            RequireErrored();

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
            var reloader = MakeReloader(c => c.RetryDelay = TimeSpan.FromMilliseconds(20));

            reloader.ReloadNow();
            RequireErrored();

            Write(Flag1True);
            RequireApplied();
        }

        [Fact]
        public void StopsRetryingAfterSuccess()
        {
            Write(Truncated);
            var reloader = MakeReloader(c =>
            {
                c.RetryDelay = TimeSpan.FromMilliseconds(10);
                c.SkipUnchanged = true;
            });
            reloader.ReloadNow();
            RequireErrored();

            Write(Flag1True);
            RequireApplied();

            // After the successful reload there are no further attempts. A changed file with no
            // trigger must not be picked up.
            Write(Flag1False);
            RequireQuiet(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void NoRetryWhenRetryDelayIsZero()
        {
            Write(Truncated);
            var reloader = MakeReloader(c => c.RetryDelay = TimeSpan.Zero);
            reloader.ReloadNow();
            RequireErrored();

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

            // A reload fails. Consumers hear OnError and may move to an interrupted state.
            File.Delete(_path);
            reloader.Trigger();
            RequireErrored();

            // The file comes back with identical content. The success must be applied despite
            // SkipUnchanged, because only Apply tells the consumer the interruption is over.
            Write(Flag1True);
            reloader.Trigger();
            RequireApplied();

            // Once recovered, identical content skips again.
            reloader.Trigger();
            RequireQuiet(TimeSpan.FromMilliseconds(100));
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
            var reloader = MakeReloader(c => c.DebounceDelay = TimeSpan.FromMilliseconds(10));
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
            var reloader = MakeReloader(c => c.RetryDelay = TimeSpan.FromMilliseconds(50));
            reloader.ReloadNow();
            RequireErrored();

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
                c.DebounceDelay = TimeSpan.Zero;
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
                c.DebounceDelay = TimeSpan.Zero;
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
        public void ApplyExceptionOnTimerThreadIsLoggedAndDoesNotStopLaterReloads()
        {
            Write(Flag1True);
            var failNext = true;
            var reloader = MakeReloader(c =>
            {
                c.DebounceDelay = TimeSpan.FromMilliseconds(10);
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

            reloader.Trigger();
            AssertEventually(() => LogCapture.HasMessageWithRegex(LogLevel.Error, "Unexpected error while reloading file data"));

            reloader.Trigger();
            RequireApplied();
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
