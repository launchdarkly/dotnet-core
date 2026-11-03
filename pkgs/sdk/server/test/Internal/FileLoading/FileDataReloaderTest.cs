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
            FileDataReloaderTiming timing = null, Func<TimeSpan> clock = null)
        {
            var config = BasicReloaderConfig();
            configure?.Invoke(config);
            _reloader = new FileDataReloader(config, timing ?? TestTiming, clock);
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

        // The delays for the tests of a file that is found empty that run on the system clock. The retry
        // comes long enough after the change signal for a test to finish rewriting the file before it,
        // even on a slow machine.
        private static readonly FileDataReloaderTiming EmptyFileTiming = Timing(10, 1000, 1500);

        // The tests of how long files are held back move a clock of their own and run each load with
        // ReloadNow, so their timing does not depend on the machine. The retry delay is an hour on that
        // clock, and the retries that the loads arm wait about as long in real time, so none runs during
        // such a test.
        private static readonly TimeSpan HoldRetryDelay = TimeSpan.FromHours(1);
        private static readonly FileDataReloaderTiming HoldTiming =
            new FileDataReloaderTiming(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), HoldRetryDelay);
        private long _clockTicks = TimeSpan.FromDays(1).Ticks;

        private TimeSpan ClockNow() => TimeSpan.FromTicks(Interlocked.Read(ref _clockTicks));

        private void Advance(TimeSpan by) => Interlocked.Add(ref _clockTicks, by.Ticks);

        private const string ReadAgainLog = "reading it again";

        private const string YamlFlag1 = "flagValues:\n  flag1: true\n";
        private const string YamlFlag2 = "flagValues:\n  flag2: true\n";
        private const string YamlFlag3 = "flagValues:\n  flag3: true\n";

        private int HoldLogCount() => LogCapture.GetMessages().Count(m => m.Text.Contains(ReadAgainLog));

        // Configures the reloader for the given files, with a YAML parser that returns null for a file
        // with no content.
        private FileDataReloader MakeYamlReloader(FileDataReloaderTiming timing, params string[] paths) =>
            MakeYamlReloader(timing, null, paths);

        private FileDataReloader MakeYamlReloader(FileDataReloaderTiming timing,
            Action<FileDataReloaderConfig> configure, params string[] paths) =>
            MakeYamlReloader(timing, configure, null, paths);

        private FileDataReloader MakeYamlReloader(FileDataReloaderTiming timing,
            Action<FileDataReloaderConfig> configure, Func<TimeSpan> clock, params string[] paths)
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
            }, timing, clock);
        }

        // A reloader on the test's clock, with HoldTiming.
        private FileDataReloader MakeHoldReloader(params string[] paths) => MakeHoldReloader(null, paths);

        private FileDataReloader MakeHoldReloader(Action<FileDataReloaderConfig> configure, params string[] paths) =>
            MakeYamlReloader(HoldTiming, configure, ClockNow, paths);

        // Runs a load, and requires that it held a file back: it logged the hold and applied nothing.
        private void RequireHeld(FileDataReloader reloader)
        {
            var holds = HoldLogCount();
            var applies = ApplyCount;
            reloader.ReloadNow();
            Assert.True(HoldLogCount() > holds, "expected the load to hold a file back");
            Assert.Equal(applies, ApplyCount);
        }

        // Runs a load, and requires that it held nothing back and applied a result, which it returns.
        private FileDataMergeResult RequireLoadApplied(FileDataReloader reloader)
        {
            var holds = HoldLogCount();
            reloader.ReloadNow();
            Assert.Equal(holds, HoldLogCount());
            return RequireApplied();
        }

        // Moves the file's modification time on, so that a test's write is seen as one on a file
        // system that records the time coarsely.
        private static void MarkWritten(string path, int seconds) =>
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(seconds));

        [Fact]
        public void EmptiedFileIsHeldBackUntilItHasStayedEmptyForTheRetryDelay()
        {
            // A writer that rewrites the file in place truncates it and then writes the new content. A
            // load that finds it empty keeps the data. In YAML an empty file is valid, so applying it
            // would briefly remove every entry.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);

            // Whatever starts a load, it finds the file empty within the retry delay and holds it back.
            Advance(HoldRetryDelay - TimeSpan.FromMinutes(1));
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(1));
            Assert.Empty(RequireLoadApplied(reloader).Flags);
        }

        [Fact]
        public void EmptiedJsonFileIsReportedOnceItHasStayedEmptyForTheRetryDelay()
        {
            Write(Flag1True);
            var reloader = MakeHoldReloader(_path);
            reloader.ReloadNow();
            RequireApplied();

            Write("");
            RequireHeld(reloader);
            Assert.Empty(ErrorLogs());

            // Still empty, it is not a JSON document. The data stays.
            Advance(HoldRetryDelay);
            reloader.ReloadNow();
            Assert.Contains("no content", RequireErrorLogged());
            Assert.Equal(1, ApplyCount);
        }

        [Fact]
        public void FileWrittenSinceItWasFoundEmptyIsHeldBackForAnotherRetryDelay()
        {
            // The file is rewritten and truncated again between two loads, as a second in-place rewrite
            // does, and no load reads the content in between.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();
            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(50));
            File.WriteAllText(yamlPath, YamlFlag2);
            File.WriteAllText(yamlPath, "");
            MarkWritten(yamlPath, 2);
            RequireHeld(reloader);

            // An hour and forty minutes after the first emptying, but fifty minutes after the second.
            Advance(TimeSpan.FromMinutes(50));
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(10));
            Assert.Empty(RequireLoadApplied(reloader).Flags);
        }

        [Fact]
        public void TwoQuickRewritesNeverApplyTheEmptyFile()
        {
            // The write of the first rewrite and the truncation of the second can fall between two
            // loads, so two loads in a row find the file empty.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);
            Advance(TimeSpan.FromMinutes(1));
            File.WriteAllText(yamlPath, YamlFlag2);
            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(1));
            File.WriteAllText(yamlPath, YamlFlag1);
            Assert.Equal(new[] { "flag1" }, FlagKeys(RequireLoadApplied(reloader)));
        }

        [Fact]
        public void ChangeToAnotherFileDoesNotApplyAFileThatIsBeingRewritten()
        {
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            var reloader = MakeHoldReloader(aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            RequireHeld(reloader);

            // Shortly before the retry delay is over, b.yaml changes. The load for that change finds
            // a.yaml still empty and applies nothing.
            Advance(HoldRetryDelay - TimeSpan.FromMinutes(5));
            File.WriteAllText(bPath, YamlFlag3);
            RequireHeld(reloader);

            File.WriteAllText(aPath, YamlFlag1);
            Assert.Equal(new[] { "flag1", "flag3" }, FlagKeys(RequireLoadApplied(reloader)).OrderBy(k => k).ToArray());
        }

        [Fact]
        public void FilesEmptiedTogetherAreAppliedTogether()
        {
            var paths = new[] { _dir.PathOf("a.yaml"), _dir.PathOf("b.yaml"), _dir.PathOf("c.yaml") };
            File.WriteAllText(paths[0], YamlFlag1);
            File.WriteAllText(paths[1], YamlFlag2);
            File.WriteAllText(paths[2], YamlFlag3);
            var reloader = MakeHoldReloader(paths);
            reloader.ReloadNow();
            RequireApplied();

            // Every file is read before any is held back, so all three start their delays in the same
            // load, and the load after the delay applies them all, instead of one file per delay.
            foreach (var path in paths)
            {
                File.WriteAllText(path, "");
            }
            RequireHeld(reloader);
            Assert.Equal(3, HoldLogCount());

            Advance(HoldRetryDelay);
            Assert.Empty(RequireLoadApplied(reloader).Flags);
        }

        [Fact]
        public void FileThatKeepsBeingWrittenWhileEmptyIsAppliedAfterFiveRetryDelays()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            // Every load finds the file empty and written since the load before it, which starts its
            // delay over each time.
            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);
            for (var i = 1; i < 10; i++)
            {
                Advance(TimeSpan.FromMinutes(30));
                MarkWritten(yamlPath, 1);
                RequireHeld(reloader);
            }

            Advance(TimeSpan.FromMinutes(30));
            MarkWritten(yamlPath, 1);
            Assert.Empty(RequireLoadApplied(reloader).Flags);
        }

        [Fact]
        public void FilesRewrittenInTurnAreAppliedAfterFiveRetryDelays()
        {
            // Two files are rewritten one after the other, so that every load finds one of them empty.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            var reloader = MakeHoldReloader(aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            RequireHeld(reloader);
            for (var i = 1; i < 10; i++)
            {
                Advance(TimeSpan.FromMinutes(30));
                File.WriteAllText(i % 2 == 1 ? aPath : bPath, i % 2 == 1 ? YamlFlag1 : YamlFlag2);
                File.WriteAllText(i % 2 == 1 ? bPath : aPath, "");
                RequireHeld(reloader);
            }

            // Five retry delays after the first of these loads, the loads stop holding files back.
            Advance(TimeSpan.FromMinutes(30));
            File.WriteAllText(bPath, YamlFlag2);
            File.WriteAllText(aPath, "");
            Assert.Equal(new[] { "flag2" }, FlagKeys(RequireLoadApplied(reloader)));
        }

        [Fact]
        public void FailedLoadDoesNotRestartTheBoundOfAFileThatIsHeldBack()
        {
            // a.yaml keeps being written while empty. Another file fails to load now and then, which
            // must not give a.yaml a fresh bound.
            var aPath = _dir.PathOf("a.yaml");
            var cPath = _dir.PathOf("c.json");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(cPath, Flag1False.Replace("flag1", "flag3"));
            var reloader = MakeHoldReloader(aPath, cPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            RequireHeld(reloader);
            for (var i = 1; i < 10; i++)
            {
                Advance(TimeSpan.FromMinutes(30));
                MarkWritten(aPath, 1);
                File.WriteAllText(cPath, i % 2 == 1 ? "{bad" : Flag1False.Replace("flag1", "flag3"));
                reloader.ReloadNow();
                Assert.Equal(1, ApplyCount);
            }

            Advance(TimeSpan.FromMinutes(30));
            MarkWritten(aPath, 1);
            File.WriteAllText(cPath, Flag1False.Replace("flag1", "flag3"));
            Assert.Equal(new[] { "flag3" }, FlagKeys(RequireLoadApplied(reloader)));
        }

        [Fact]
        public void AnotherFilesBoundDoesNotCutShortARewrite()
        {
            // b.yaml keeps being written while empty from the start, and reaches its bound while a.yaml
            // is in the middle of an ordinary rewrite. a.yaml is still held back.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            var reloader = MakeHoldReloader(aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(bPath, "");
            RequireHeld(reloader);
            for (var i = 1; i <= 9; i++)
            {
                Advance(TimeSpan.FromMinutes(30));
                MarkWritten(bPath, 1);
                if (i == 9)
                {
                    File.WriteAllText(aPath, "");
                }
                RequireHeld(reloader);
            }

            Advance(TimeSpan.FromMinutes(30));
            MarkWritten(bPath, 1);
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(15));
            File.WriteAllText(aPath, YamlFlag3);
            Assert.Equal(new[] { "flag3" }, FlagKeys(RequireLoadApplied(reloader)));
        }

        [Fact]
        public void SlowReadOfAnotherFileDoesNotShortenTheDelay()
        {
            // The read of b.yaml, which comes first, takes most of a retry delay. a.yaml's delay is
            // counted from when it was read.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            var reader = new ClockAdvancingReader(this);
            var reloader = MakeHoldReloader(c => c.FileReader = reader, bPath, aPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            reader.AdvanceOnNextRead(bPath, HoldRetryDelay - TimeSpan.FromMinutes(6));
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(10));
            RequireHeld(reloader);
        }

        // Reads files as the default reader does, and moves the test's clock on while it reads a given
        // file once.
        private sealed class ClockAdvancingReader : FileDataTypes.IFileReader
        {
            private readonly FileDataReloaderTest _test;
            private string _path;
            private TimeSpan _by;

            internal ClockAdvancingReader(FileDataReloaderTest test) => _test = test;

            internal void AdvanceOnNextRead(string path, TimeSpan by)
            {
                _path = path;
                _by = by;
            }

            public string ReadAllText(string path)
            {
                var content = FileDataReader.Instance.ReadAllText(path);
                if (path == _path)
                {
                    _path = null;
                    _test.Advance(_by);
                }
                return content;
            }
        }

        [Fact]
        public void FileEmptiedAgainAfterContentIsHeldBackAgainWhenItsStateLooksTheSame()
        {
            // On a file system that records modification times coarsely, a file emptied a second time
            // can look exactly as it did the first time. The read of its content in between still
            // starts its delay over. b.yaml is kept written while empty, so that no load applies.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            var reloader = MakeHoldReloader(aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            var firstEmptyWriteTime = File.GetLastWriteTimeUtc(aPath);
            File.WriteAllText(bPath, "");
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(50));
            File.WriteAllText(aPath, YamlFlag1);
            MarkWritten(bPath, 1);
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(20));
            File.WriteAllText(aPath, "");
            File.SetLastWriteTimeUtc(aPath, firstEmptyWriteTime);
            MarkWritten(bPath, 1);
            RequireHeld(reloader);

            // b.yaml gets its content back while a.yaml is still within its new delay.
            Advance(TimeSpan.FromMinutes(10));
            File.WriteAllText(bPath, YamlFlag2);
            RequireHeld(reloader);
        }

        [Fact]
        public void FileRewrittenWithTheSameContentIsHeldBackAgainLater()
        {
            // A tool rewrites the file in place now and then with the same content, so the load after
            // the rewrite is skipped as unchanged. Each rewrite is held back with a bound of its own.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(c => c.SkipUnchanged = true, yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);
            File.WriteAllText(yamlPath, YamlFlag1);
            reloader.ReloadNow();
            Assert.Equal(1, ApplyCount);

            Advance(TimeSpan.FromHours(6));
            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);
        }

        [Fact]
        public void FilesEmptiedInStaggeredTurnsAreAppliedAfterTheBound()
        {
            // Two files stay empty and are truncated again in turn, so that whenever one of them has
            // stayed empty for the whole delay, the other is still within its delay and holds the load
            // back. A file that settles so does not get a fresh bound while the other is held back.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            var reloader = MakeHoldReloader(aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            RequireHeld(reloader);
            var step = TimeSpan.FromMinutes(54);
            var applied = false;
            for (var i = 1; i <= 14 && !applied; i++)
            {
                Advance(step);
                MarkWritten(i % 2 == 1 ? bPath : aPath, 1);
                if (i == 1)
                {
                    File.WriteAllText(bPath, "");
                }
                reloader.ReloadNow();
                // The retry that the hold arms, a little after the other file's delay is over.
                Advance(TimeSpan.FromMinutes(12));
                reloader.ReloadNow();
                Advance(-TimeSpan.FromMinutes(12));
                applied = ApplyCount > 1;
            }
            Assert.True(applied, "the files were held back without end");
        }

        [Fact]
        public void BoundOfAFileHeldBackDuringAFailureStartsOverOnceItIsReadAgain()
        {
            // While b.json fails to load, a.yaml is rewritten once and its gap is held back. Later, b.json
            // is fixed while a.yaml is in the middle of another rewrite, which is held back with a bound
            // of its own.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.json");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, Flag1False.Replace("flag1", "flag3"));
            var reloader = MakeHoldReloader(aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(bPath, "{bad");
            File.WriteAllText(aPath, "");
            reloader.ReloadNow();
            File.WriteAllText(aPath, YamlFlag2);
            reloader.ReloadNow();
            Advance(TimeSpan.FromHours(6));
            reloader.ReloadNow();

            File.WriteAllText(bPath, Flag1False.Replace("flag1", "flag3"));
            File.WriteAllText(aPath, "");
            RequireHeld(reloader);
        }

        [Fact]
        public void HoldAsksForTheRetryFromTheEndOfTheLoad()
        {
            // The read of the file after the held one takes longer than what is left of the held
            // file's delay, so the retry is due right away, after the margin.
            var tenth = TimeSpan.FromTicks(HoldRetryDelay.Ticks / 10);
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            var reader = new ClockAdvancingReader(this);
            var reloader = MakeHoldReloader(c => c.FileReader = reader, aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            RequireHeld(reloader);
            Advance(TimeSpan.FromMinutes(57));
            reader.AdvanceOnNextRead(bPath, TimeSpan.FromMinutes(6));
            RequireHeld(reloader);
            Assert.Equal(tenth, reloader.LastRequestedRetryDelay);
        }

        [Theory]
        [InlineData(".yaml")]
        [InlineData(".json")]
        public void FixOfAFileSavedInPlaceWithAnErrorIsHeldBack(string extension)
        {
            // An operator saves the file in place with a mistake, and saves the fix in place long after.
            // The loads in between fail on the file, and the fix's own gap is held back.
            var path = _dir.PathOf("data" + extension);
            var good = extension == ".yaml" ? YamlFlag1 : Flag1True;
            var fixedContent = extension == ".yaml" ? YamlFlag2 : Flag1False;
            var broken = extension == ".yaml" ? "flagValues: [\n" : @"{""flagValues"": ";
            File.WriteAllText(path, good);
            var reloader = MakeHoldReloader(path);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(path, "");
            RequireHeld(reloader);
            File.WriteAllText(path, broken);
            reloader.ReloadNow();
            RequireErrorLogged();
            for (var i = 0; i < 6; i++)
            {
                Advance(HoldRetryDelay);
                reloader.ReloadNow();
            }
            DrainErrors();

            File.WriteAllText(path, "");
            RequireHeld(reloader);
            Assert.Equal(_consumedErrorLogs, ErrorLogs().Count);
            File.WriteAllText(path, fixedContent);
            RequireLoadApplied(reloader);
        }

        [Fact]
        public void FileThatAFailingLoadDidNotReachKeepsItsBound()
        {
            // c.json comes first and fails now and then, so those loads never read a.yaml, which keeps
            // being written while empty. They do not give a.yaml a fresh bound.
            var cPath = _dir.PathOf("c.json");
            var aPath = _dir.PathOf("a.yaml");
            var cGood = Flag1False.Replace("flag1", "flag3");
            File.WriteAllText(cPath, cGood);
            File.WriteAllText(aPath, YamlFlag1);
            var reloader = MakeHoldReloader(cPath, aPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            RequireHeld(reloader);
            for (var i = 1; i < 10; i++)
            {
                Advance(TimeSpan.FromMinutes(30));
                MarkWritten(aPath, 1);
                File.WriteAllText(cPath, i % 2 == 1 ? "{bad" : cGood);
                reloader.ReloadNow();
            }

            Advance(TimeSpan.FromMinutes(30));
            MarkWritten(aPath, 1);
            File.WriteAllText(cPath, cGood);
            Assert.Equal(new[] { "flag3" }, FlagKeys(RequireLoadApplied(reloader)));
        }

        [Fact]
        public void LoadThatHoldsAFileBackAndThenFailsDoesNotStartBoundsOver()
        {
            // a.yaml and b.yaml are rewritten in turn, so that every load holds one of them back, and
            // c.json fails at every other load. Those loads hold a file back, so they start no bound over.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            var cPath = _dir.PathOf("c.json");
            var cGood = Flag1False.Replace("flag1", "flag3");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            File.WriteAllText(cPath, cGood);
            var reloader = MakeHoldReloader(aPath, bPath, cPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            RequireHeld(reloader);
            var applied = false;
            for (var i = 1; i <= 12 && !applied; i++)
            {
                Advance(TimeSpan.FromMinutes(30));
                File.WriteAllText(i % 2 == 1 ? aPath : bPath, i % 2 == 1 ? YamlFlag1 : YamlFlag2);
                File.WriteAllText(i % 2 == 1 ? bPath : aPath, "");
                File.WriteAllText(cPath, i % 2 == 1 ? "{bad" : cGood);
                reloader.ReloadNow();
                applied = ApplyCount > 1;
            }
            Assert.True(applied, "the files were held back without end");
        }

        [Fact]
        public void FileFoundMissingStartsANewDelayWhenItIsCreatedEmptyAgain()
        {
            // While b.json fails, a.yaml is emptied, deleted, and created empty again with the state it had
            // when it was first found empty, as within one tick of a coarse clock. Being missing ended its
            // empty state, so it is held back with a new delay.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.json");
            var bGood = Flag1False.Replace("flag1", "flag3");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, bGood);
            var reloader = MakeHoldReloader(c => c.SkipMissingPaths = true, aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(bPath, "{bad");
            File.WriteAllText(aPath, "");
            var firstEmptyWriteTime = File.GetLastWriteTimeUtc(aPath);
            reloader.ReloadNow();
            Advance(TimeSpan.FromMinutes(30));
            File.Delete(aPath);
            reloader.ReloadNow();
            Advance(TimeSpan.FromMinutes(40));
            File.WriteAllText(aPath, "");
            File.SetLastWriteTimeUtc(aPath, firstEmptyWriteTime);
            reloader.ReloadNow();

            File.WriteAllText(bPath, bGood);
            RequireHeld(reloader);
        }

        [Fact]
        public void HoldAsksForTheRetryWhenTheDelayIsOver()
        {
            var tenth = TimeSpan.FromTicks(HoldRetryDelay.Ticks / 10);
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);
            Assert.Equal(HoldRetryDelay + tenth, reloader.LastRequestedRetryDelay);

            // A later load asks for the rest of the delay, not a whole one.
            Advance(TimeSpan.FromMinutes(30));
            RequireHeld(reloader);
            Assert.Equal(TimeSpan.FromMinutes(30) + tenth, reloader.LastRequestedRetryDelay);
        }

        [Fact]
        public void HoldAsksForTheRetryWhenTheBoundIsOver()
        {
            var tenth = TimeSpan.FromTicks(HoldRetryDelay.Ticks / 10);
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);
            for (var i = 0; i < 4; i++)
            {
                Advance(TimeSpan.FromMinutes(60));
                MarkWritten(yamlPath, 1);
                RequireHeld(reloader);
            }
            Advance(TimeSpan.FromMinutes(48));
            MarkWritten(yamlPath, 1);
            RequireHeld(reloader);

            // The file's delay would be over in an hour, but its bound is over in 12 minutes.
            Assert.Equal(TimeSpan.FromMinutes(12) + tenth, reloader.LastRequestedRetryDelay);
        }

        [Fact]
        public void HoldOfSeveralFilesAsksForTheRetryWhenTheFirstIsDue()
        {
            var tenth = TimeSpan.FromTicks(HoldRetryDelay.Ticks / 10);
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            File.WriteAllText(bPath, YamlFlag2);
            var reloader = MakeHoldReloader(aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(aPath, "");
            RequireHeld(reloader);
            Advance(TimeSpan.FromMinutes(20));
            File.WriteAllText(bPath, "");
            RequireHeld(reloader);
            Assert.Equal(TimeSpan.FromMinutes(40) + tenth, reloader.LastRequestedRetryDelay);
        }

        [Fact]
        public void HoldStartedByAChangeSignalAsksForTheRetryWhenTheDelayIsOver()
        {
            var tenth = TimeSpan.FromTicks(HoldRetryDelay.Ticks / 10);
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            reloader.Trigger();
            AssertEventually(() => HoldLogCount() == 1);
            AssertEventually(() => reloader.LastRequestedRetryDelay == HoldRetryDelay + tenth);
        }

        [Fact]
        public void FailedLoadAsksForTheRetryDelay()
        {
            Write(Flag1True);
            var reloader = MakeHoldReloader(_path);
            reloader.ReloadNow();
            RequireApplied();

            Write(Truncated);
            reloader.ReloadNow();
            RequireErrorLogged();
            Assert.Equal(HoldRetryDelay, reloader.LastRequestedRetryDelay);
        }

        [Fact]
        public void FileEmptiedAfterAnEarlierEmptyingWasReportedIsHeldBackAgain()
        {
            // A JSON file stays empty past the retry delay, so it is reported, and stays so for hours.
            Write(Flag1True);
            var reloader = MakeHoldReloader(_path);
            reloader.ReloadNow();
            RequireApplied();
            Write("");
            RequireHeld(reloader);
            Advance(HoldRetryDelay);
            reloader.ReloadNow();
            Assert.Contains("no content", RequireErrorLogged());
            Advance(TimeSpan.FromHours(10));

            // The file is then rewritten in place. The load that finds it truncated holds it back
            // rather than reporting it again.
            File.WriteAllText(_path, "");
            MarkWritten(_path, 5);
            RequireHeld(reloader);
            Assert.Equal(_consumedErrorLogs, ErrorLogs().Count);

            File.WriteAllText(_path, Flag1False);
            var flag = Assert.IsType<FeatureFlag>(RequireLoadApplied(reloader).Flags[0].Value.Item);
            Assert.Equal(new[] { LdValue.Of(false) }, flag.Variations);
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void WriteToTheTargetOfALinkStartsTheDelayOver()
        {
            // The configured path is a symbolic link, whose own modification time never changes. The
            // writes go to its target.
            var target = _dir.PathOf("target.yaml");
            var link = _dir.PathOf("data.yaml");
            File.WriteAllText(target, YamlFlag1);
            if (!SymbolicLinks.TryCreateFileLink(link, target))
            {
                return;
            }
            var reloader = MakeHoldReloader(link);
            reloader.ReloadNow();
            RequireApplied();
            File.WriteAllText(target, "");
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(50));
            File.WriteAllText(target, YamlFlag2);
            File.WriteAllText(target, "");
            MarkWritten(target, 2);
            RequireHeld(reloader);

            Advance(TimeSpan.FromMinutes(50));
            RequireHeld(reloader);
        }
#endif

        [Fact]
        public void FileEmptiedAgainAfterItHadContentIsHeldBackAgain()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();
            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);
            File.WriteAllText(yamlPath, YamlFlag2);
            Assert.Equal(new[] { "flag2" }, FlagKeys(RequireLoadApplied(reloader)));

            // Long after, the file is emptied again. That is a new rewrite, held back like the first.
            Advance(TimeSpan.FromHours(10));
            File.WriteAllText(yamlPath, "");
            RequireHeld(reloader);
        }

        [Theory]
        [InlineData(" \n")]
        [InlineData("# written first\n")]
        [InlineData("---\n")]
        public void FileWithNoEntriesInItsContentIsHeldBack(string content)
        {
            // White space, or YAML that has no document yet, is what a read can see of a file that is
            // being written.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, content);
            RequireHeld(reloader);
        }

        [Fact]
        public void JsonFileWithOnlyWhiteSpaceIsHeldBack()
        {
            // White space is not a JSON document, but it is held back like an empty file rather than
            // reported, since a rewrite can leave it for a moment.
            Write(Flag1True);
            var reloader = MakeHoldReloader(_path);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(_path, " \n");
            RequireHeld(reloader);
            Assert.Empty(ErrorLogs());
        }

        [Fact]
        public void DocumentWithNoEntriesIsAppliedRightAway()
        {
            // An empty mapping is a whole document, which removes the entries on purpose.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "{}\n");
            Assert.Empty(RequireLoadApplied(reloader).Flags);
        }

        [Fact]
        public void FileThatIsEmptyFromTheStartIsNotHeldBack()
        {
            // An empty YAML file next to the data, such as a placeholder for later entries, has never
            // had content in effect. It does not hold back the changes of the other file.
            var placeholderPath = _dir.PathOf("placeholder.yaml");
            File.WriteAllText(_path, Flag1True);
            File.WriteAllText(placeholderPath, "");
            var reloader = MakeHoldReloader(_path, placeholderPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(_path, Flag1False);
            var flag = Assert.IsType<FeatureFlag>(RequireLoadApplied(reloader).Flags[0].Value.Item);
            Assert.Equal(new[] { LdValue.Of(false) }, flag.Variations);
            Assert.Equal(0, HoldLogCount());
        }

        [Fact]
        public void FileThatIsDeletedAndCreatedEmptyIsNotHeldBack()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(c => c.SkipMissingPaths = true, yamlPath);
            reloader.ReloadNow();
            RequireApplied();
            File.Delete(yamlPath);
            Assert.Empty(RequireLoadApplied(reloader).Flags);

            // A missing file had no content in effect, so the empty file that replaces it is applied.
            File.WriteAllText(yamlPath, "");
            Assert.Empty(RequireLoadApplied(reloader).Flags);
        }

        [Fact]
        public void FileThatContentMovedToIsHeldBackWhenItIsEmptied()
        {
            // Both paths are configured, and the content moves from one to the other.
            var aPath = _dir.PathOf("a.yaml");
            var bPath = _dir.PathOf("b.yaml");
            File.WriteAllText(aPath, YamlFlag1);
            var reloader = MakeHoldReloader(c =>
            {
                c.SkipMissingPaths = true;
                c.SkipUnchanged = true;
            }, aPath, bPath);
            reloader.ReloadNow();
            RequireApplied();
            File.Move(aPath, bPath);
            reloader.ReloadNow();

            File.WriteAllText(bPath, "");
            RequireHeld(reloader);
        }

        [Fact]
        public void EmptyFileIsNotHeldBackWhenRetryIsOff()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeHoldReloader(c => c.RetryOnFailure = false, yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            // Without a retry, nothing would read the file again, so the load applies it as it is.
            File.WriteAllText(yamlPath, "");
            Assert.Empty(RequireLoadApplied(reloader).Flags);
        }

        [Fact]
        public void EmptiedFileIsHeldBackAndTheRetryAppliesTheRewrite()
        {
            // On the system clock: a change signal starts the load that finds the file empty, and the
            // retry that the hold arms reads the rewritten file.
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, yamlPath);
            reloader.ReloadNow();
            RequireApplied();

            File.WriteAllText(yamlPath, "");
            reloader.Trigger();
            AssertEventually(() => HoldLogCount() == 1);
            File.WriteAllText(yamlPath, YamlFlag2);
            Assert.Equal(new[] { "flag2" }, FlagKeys(RequireApplied()));
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
            AssertEventually(() => HoldLogCount() == 1);

            // The file is still empty when the retry reads it, so it is a document with no entries.
            Assert.Empty(RequireApplied().Flags);
            AssertLogMessageRegex(true, LogLevel.Debug, "Reading file data again after a file was found empty");
        }

        [Fact]
        public void RetryThatFindsAFileEmptiedHoldsItBack()
        {
            // A retry left over from a failure can run while the file is being rewritten in place. It
            // holds the file back too, rather than applying it empty.
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
            AssertEventually(() => HoldLogCount() == 1);
            File.WriteAllText(yamlPath, YamlFlag2);
            Assert.Equal(new[] { "flag2" }, FlagKeys(RequireApplied()));
        }

        [Fact]
        public void RetryLeftOverFromAHoldDoesNotLoadAfterASuccessfulReloadNow()
        {
            var yamlPath = _dir.PathOf("data.yaml");
            File.WriteAllText(yamlPath, YamlFlag1);
            var reloader = MakeYamlReloader(EmptyFileTiming, yamlPath);
            reloader.ReloadNow();
            RequireApplied();
            File.WriteAllText(yamlPath, "");
            reloader.Trigger();
            AssertEventually(() => HoldLogCount() == 1);

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
        public void HoldingAFileBackDoesNotUseUpALoadAttempt()
        {
            Write(Flag1True);
            var reader = new CountingFileReader();
            var reloader = MakeReloader(c =>
            {
                c.FileReader = reader;
                c.MaxLoadAttempts = 2;
            }, Timing(10, 1000, 200));
            reloader.ReloadNow();
            RequireApplied();

            Write("");
            reloader.Trigger();

            // After the initial load: the hold, then the two attempts of the budget.
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
