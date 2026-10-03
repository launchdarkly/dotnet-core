using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataPollerTest : BaseTest, IDisposable
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(100);

        private readonly TempDirectory _dir = TempDirectory.Create();
        private readonly EventSink<bool> _changed = new EventSink<bool>();
        private FileDataPoller _poller;

        public FileDataPollerTest(ITestOutputHelper testOutput) : base(testOutput) { }

        public void Dispose()
        {
            _poller?.Dispose();
            _dir.Dispose();
        }

        private FileDataPoller StartPoller(params string[] paths)
        {
            _poller = new FileDataPoller(paths, PollInterval, () => _changed.Enqueue(true), TestLogger);
            return _poller;
        }

        private void RequireChange() => _changed.ExpectValue(TestTimeout);

        private void RequireNoChange(TimeSpan duration) => _changed.ExpectNoValue(duration);

        // Rewrites a file and guarantees the observed (modification time, size) state differs from
        // the previous state, so the poller must detect it regardless of timestamp granularity.
        private static void WriteWithNewModTime(string path, string content) =>
            WriteWithModTime(path, content, DateTime.UtcNow.AddSeconds(content.Length));

        // Writes the content and the modification time to a temporary file, then renames it over
        // the target. The poller observes one change, not one for the content and one for the time.
        private static void WriteWithModTime(string path, string content, DateTime modTime)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, content);
            File.SetLastWriteTimeUtc(temp, modTime);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        [Fact]
        public void DetectsModification()
        {
            var path = _dir.PathOf("data.json");
            WriteWithNewModTime(path, "one");
            StartPoller(path);

            RequireNoChange(QuietPeriod);

            WriteWithNewModTime(path, "two!");
            RequireChange();
        }

        [Fact]
        public void DetectsSameSizeRewriteWithNewModTime()
        {
            var path = _dir.PathOf("data.json");
            var baseTime = DateTime.UtcNow.AddHours(-1);
            WriteWithModTime(path, "one", baseTime);
            StartPoller(path);

            WriteWithModTime(path, "two", baseTime.AddMinutes(1));
            RequireChange();
        }

        [Fact]
        public void DetectsSizeChangeWithSameModTime()
        {
            var path = _dir.PathOf("data.json");
            var baseTime = DateTime.UtcNow.AddHours(-1);
            WriteWithModTime(path, "one", baseTime);
            StartPoller(path);

            WriteWithModTime(path, "four", baseTime);
            RequireChange();
        }

        [Fact]
        public void FiresOncePerChange()
        {
            var path = _dir.PathOf("data.json");
            WriteWithNewModTime(path, "one");
            StartPoller(path);

            WriteWithNewModTime(path, "two!");
            RequireChange();

            RequireNoChange(QuietPeriod);
        }

        [Fact]
        public void DetectsFileAppearing()
        {
            var path = _dir.PathOf("data.json");
            StartPoller(path);

            RequireNoChange(QuietPeriod);

            WriteWithNewModTime(path, "created");
            RequireChange();
        }

        [Fact]
        public void DetectsFileDisappearing()
        {
            var path = _dir.PathOf("data.json");
            WriteWithNewModTime(path, "content");
            StartPoller(path);

            File.Delete(path);
            RequireChange();
        }

        [Fact]
        public void WatchesAllFiles()
        {
            var path1 = _dir.PathOf("one.json");
            var path2 = _dir.PathOf("two.json");
            WriteWithNewModTime(path1, "one");
            WriteWithNewModTime(path2, "two");
            StartPoller(path1, path2);

            WriteWithNewModTime(path2, "two-changed");
            RequireChange();
        }

        [Fact]
        public void DetectsChangeToFirstOfMultipleFiles()
        {
            var path1 = _dir.PathOf("one.json");
            var path2 = _dir.PathOf("two.json");
            WriteWithNewModTime(path1, "one");
            WriteWithNewModTime(path2, "two");
            StartPoller(path1, path2);

            WriteWithNewModTime(path1, "one-changed");
            RequireChange();
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void DetectsAChangeToTheTargetOfALink()
        {
            var target = _dir.PathOf("target.json");
            var baseTime = DateTime.UtcNow.AddHours(-1);
            WriteWithModTime(target, "one", baseTime);
            var link = _dir.PathOf("data.json");
            if (!SymbolicLinks.TryCreateFileLink(link, target))
            {
                TestLogger.Info("symbolic links cannot be created in this environment; skipping");
                return;
            }
            StartPoller(link);

            RequireNoChange(QuietPeriod);

            // The target changes. The link itself is untouched.
            WriteWithModTime(target, "two!", baseTime.AddMinutes(1));
            RequireChange();
        }

        [Fact]
        public void DetectsAReplacedDirectoryLinkInThePath()
        {
            // The layout of a mounted ConfigMap: the configured file is a link into a data directory
            // that is itself a link to the current version. An update writes a new version and
            // replaces the data directory link. The configured file is never touched.
            if (!SymbolicLinks.CanReplace)
            {
                TestLogger.Info("directory links cannot be replaced atomically in this environment; skipping");
                return;
            }
            var version1 = _dir.PathOf("..version1");
            Directory.CreateDirectory(version1);
            File.WriteAllText(Path.Combine(version1, "data.json"), "one");
            var configured = _dir.PathOf("data.json");
            if (!SymbolicLinks.TryCreateDirectoryLink(_dir.PathOf("..data"), "..version1") ||
                !SymbolicLinks.TryCreateFileLink(configured, Path.Combine("..data", "data.json")))
            {
                TestLogger.Info("symbolic links cannot be created in this environment; skipping");
                return;
            }
            StartPoller(configured);

            RequireNoChange(QuietPeriod);

            var version2 = _dir.PathOf("..version2");
            Directory.CreateDirectory(version2);
            File.WriteAllText(Path.Combine(version2, "data.json"), "two!");
            SymbolicLinks.TryCreateDirectoryLink(_dir.PathOf("..data_tmp"), "..version2");
            SymbolicLinks.Replace(_dir.PathOf("..data_tmp"), _dir.PathOf("..data"));
            RequireChange();
        }
#endif

        [Fact]
        public void StopsOnDispose()
        {
            var path = _dir.PathOf("data.json");
            WriteWithNewModTime(path, "one");
            var poller = StartPoller(path);

            poller.Dispose();
            poller.Dispose();

            WriteWithNewModTime(path, "two!");
            RequireNoChange(QuietPeriod);
        }

        [Fact]
        public void DisposeReturnsWhileCallbackBlocks()
        {
            var path = _dir.PathOf("data.json");
            WriteWithNewModTime(path, "one");
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            _poller = new FileDataPoller(new[] { path }, PollInterval, () =>
            {
                entered.Set();
                release.Wait();
            }, TestLogger);

            WriteWithNewModTime(path, "two!");
            Assert.True(entered.Wait(TestTimeout), "timed out waiting for the callback to start");

            var disposed = Task.Run(() => _poller.Dispose());
            Assert.True(disposed.Wait(TestTimeout), "Dispose blocked on a callback in progress");

            release.Set();
        }

        [Fact]
        public void CallbackExceptionIsLoggedAndPollingContinues()
        {
            var path = _dir.PathOf("data.json");
            WriteWithNewModTime(path, "one");
            var calls = 0;
            _poller = new FileDataPoller(new[] { path }, PollInterval, () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("consumer failed");
                }
                _changed.Enqueue(true);
            }, TestLogger);

            WriteWithNewModTime(path, "two!");
            // The poller logs the exception on its own thread after the callback throws, so the
            // test waits for the log line rather than for the callback count.
            var deadline = DateTime.UtcNow + TestTimeout;
            while (!LogCapture.HasMessageWithRegex(Logging.LogLevel.Error, "Unexpected error while examining files")
                && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }
            AssertLogMessageRegex(true, Logging.LogLevel.Error, "Unexpected error while examining files");
            Assert.True(Volatile.Read(ref calls) >= 1, "the callback was not invoked");

            // The exception did not stop the poller: a later change is still reported.
            WriteWithNewModTime(path, "three");
            RequireChange();
        }
    }
}
