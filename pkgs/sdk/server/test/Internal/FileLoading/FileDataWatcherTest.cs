using System;
using System.IO;
using System.Threading;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataWatcherTest : BaseTest, IDisposable
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly TempDirectory _dir = TempDirectory.Create();
        private readonly EventSink<bool> _changed = new EventSink<bool>();
        private FileDataWatcher _watcher;

        public FileDataWatcherTest(ITestOutputHelper testOutput) : base(testOutput) { }

        public void Dispose()
        {
            _watcher?.Dispose();
            _dir.Dispose();
        }

        private FileDataWatcher StartWatcher(params string[] paths)
        {
            _watcher = new FileDataWatcher(paths, () => _changed.Enqueue(true), TestLogger, RetryDelay);
            return _watcher;
        }

        private void RequireChange() => _changed.ExpectValue(TestTimeout);

        private void RequireLogMessage(Logging.LogLevel level, string pattern)
        {
            var deadline = DateTime.UtcNow + TestTimeout;
            while (!LogCapture.HasMessageWithRegex(level, pattern) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
            AssertLogMessageRegex(true, level, pattern);
        }

        [Fact]
        public void DetectsModification()
        {
            var path = _dir.PathOf("data.json");
            File.WriteAllText(path, "one");
            StartWatcher(path);

            File.WriteAllText(path, "two");
            RequireChange();
        }

        [Fact]
        public void DetectsFileAppearing()
        {
            var path = _dir.PathOf("data.json");
            StartWatcher(path);

            File.WriteAllText(path, "created");
            RequireChange();
        }

        [Fact]
        public void DetectsFileDisappearing()
        {
            var path = _dir.PathOf("data.json");
            File.WriteAllText(path, "one");
            StartWatcher(path);

            File.Delete(path);
            RequireChange();
        }

        [Fact]
        public void DetectsFileReplacedByRename()
        {
            var path = _dir.PathOf("data.json");
            var temp = _dir.PathOf("data.json.tmp");
            File.WriteAllText(path, "one");
            StartWatcher(path);

            File.WriteAllText(temp, "two");
            File.Delete(path);
            File.Move(temp, path);
            RequireChange();
        }

        [Fact]
        public void IgnoresOtherFilesInTheSameDirectory()
        {
            var path = _dir.PathOf("data.json");
            var other = _dir.PathOf("other.json");
            File.WriteAllText(path, "one");
            StartWatcher(path);

            File.WriteAllText(other, "unrelated");
            File.WriteAllText(other, "unrelated again");
            _changed.ExpectNoValue(QuietPeriod);
        }

        [Fact]
        public void WatchesFilesInDifferentDirectories()
        {
            var subdir = _dir.PathOf("sub");
            Directory.CreateDirectory(subdir);
            var path1 = _dir.PathOf("one.json");
            var path2 = Path.Combine(subdir, "two.json");
            File.WriteAllText(path1, "one");
            File.WriteAllText(path2, "two");
            StartWatcher(path1, path2);

            File.WriteAllText(path2, "two-changed");
            RequireChange();
        }

        [Fact]
        public void DetectsChangeToFirstOfMultipleFiles()
        {
            var path1 = _dir.PathOf("one.json");
            var path2 = _dir.PathOf("two.json");
            File.WriteAllText(path1, "one");
            File.WriteAllText(path2, "two");
            StartWatcher(path1, path2);

            File.WriteAllText(path1, "one-changed");
            RequireChange();
        }

        [Fact]
        public void RelativePathsAreResolvedAgainstTheCurrentDirectory()
        {
            var name = "ld-watcher-test-" + Guid.NewGuid().ToString("N") + ".json";
            var fullPath = Path.GetFullPath(name);
            try
            {
                StartWatcher(name);
                File.WriteAllText(fullPath, "created");
                RequireChange();
            }
            finally
            {
                _watcher?.Dispose();
                _watcher = null;
                File.Delete(fullPath);
            }
        }

        [Fact]
        public void StopsOnDispose()
        {
            var path = _dir.PathOf("data.json");
            File.WriteAllText(path, "one");
            var watcher = StartWatcher(path);

            watcher.Dispose();
            watcher.Dispose();

            File.WriteAllText(path, "two");
            _changed.ExpectNoValue(QuietPeriod);
        }

        [Fact]
        public void MissingDirectoryIsWatchedWhenItAppears()
        {
            var directory = _dir.PathOf("created-later");
            var path = Path.Combine(directory, "data.json");
            StartWatcher(path);

            // The failure is logged once. The watcher keeps running.
            AssertLogMessageRegex(true, Logging.LogLevel.Error, "Unable to watch directory");

            // Once the directory exists, the watch is set up and the file in it is reported.
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "created");
            RequireChange();
        }

        [Fact]
        public void UnwatchableDirectoryDoesNotStopTheOtherDirectories()
        {
            var inMissingDirectory = Path.Combine(_dir.PathOf("no-such-directory"), "one.json");
            var path = _dir.PathOf("two.json");
            File.WriteAllText(path, "two");
            StartWatcher(inMissingDirectory, path);

            File.WriteAllText(path, "two-changed");
            RequireChange();
        }

        [Fact]
        public void DeletedAndRecreatedDirectoryIsWatchedAgain()
        {
            var directory = _dir.PathOf("recreated");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "data.json");
            File.WriteAllText(path, "one");
            StartWatcher(path);

            // The file goes first, which is a change, and then the directory.
            File.Delete(path);
            RequireChange();
            Directory.Delete(directory);
            RequireLogMessage(Logging.LogLevel.Warn, "no longer exists");

            // The recreated directory is watched again, and the file in it is reported.
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "two");
            RequireChange();
        }

        [Fact]
        public void AcceptsANullLogger()
        {
            var path = Path.Combine(_dir.PathOf("no-such-directory"), "data.json");
            _watcher = new FileDataWatcher(new[] { path }, () => _changed.Enqueue(true), null, RetryDelay);
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void DetectsAReplacedDirectoryLinkInThePath()
        {
            // The layout of a mounted ConfigMap: the configured file is a link into a data directory
            // that is itself a link to the current version. An update writes a new version and
            // replaces the data directory link. No notification names the configured file.
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
            StartWatcher(configured);

            var version2 = _dir.PathOf("..version2");
            Directory.CreateDirectory(version2);
            File.WriteAllText(Path.Combine(version2, "data.json"), "two!");
            SymbolicLinks.TryCreateDirectoryLink(_dir.PathOf("..data_tmp"), "..version2");
            SymbolicLinks.Replace(_dir.PathOf("..data_tmp"), _dir.PathOf("..data"));
            RequireChange();
        }
#endif
    }
}
