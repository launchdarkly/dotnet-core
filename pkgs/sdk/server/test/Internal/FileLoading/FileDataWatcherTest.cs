using System;
using System.Collections.Concurrent;
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
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(50);

        private readonly TempDirectory _dir = TempDirectory.Create();
        private readonly BlockingCollection<bool> _changes = new BlockingCollection<bool>();
        private FileDataWatcher _watcher;

        public FileDataWatcherTest(ITestOutputHelper testOutput) : base(testOutput) { }

        public void Dispose()
        {
            _watcher?.Dispose();
            _dir.Dispose();
        }

        private FileDataWatcher StartWatcher(params string[] paths)
        {
            _watcher = new FileDataWatcher(paths, () => _changes.Add(true), TestLogger, CheckInterval);
            return _watcher;
        }

        private void RequireChange() =>
            Assert.True(_changes.TryTake(out _, TestTimeout), "expected a change notification");

        private void RequireNoChange() =>
            Assert.False(_changes.TryTake(out _, QuietPeriod), "expected no change notification");

        // On macOS, a notification for a write made shortly before the watch was set up can be
        // delivered after it, tens of milliseconds later, and a notification that names a
        // configured file is reported without comparing its state. A test that asserts silence
        // first lets such notifications arrive and discards them.
        private void DiscardStaleNotifications()
        {
            Thread.Sleep(QuietPeriod);
            while (_changes.TryTake(out _))
            {
            }
        }

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
            DiscardStaleNotifications();

            File.WriteAllText(other, "unrelated");
            File.WriteAllText(other, "unrelated again");
            RequireNoChange();
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
            DiscardStaleNotifications();

            watcher.Dispose();
            watcher.Dispose();

            File.WriteAllText(path, "two");
            RequireNoChange();
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
        public void DeletedDirectoryIsWatchedAgainWhenItAppears()
        {
            var directory = _dir.PathOf("recreated");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "data.json");
            File.WriteAllText(path, "one");
            StartWatcher(path);

            // The file goes first, which is a change, and then the directory. The directory stays
            // missing until the watcher has noticed and dropped its watch.
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
        public void DirectoryRemovedWithoutNotificationsIsReportedOnce()
        {
            var directory = _dir.PathOf("removed");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "data.json");
            File.WriteAllText(path, "one");
            StartWatcher(path);
            DiscardStaleNotifications();

            // Moving the directory away removes the configured file with it, and the platform
            // reports nothing. The absence of the file is a change.
            Directory.Move(directory, _dir.PathOf("removed-elsewhere"));
            RequireChange();

            // The directory stays missing. Its watch is dropped, and nothing more is reported.
            RequireLogMessage(Logging.LogLevel.Warn, "no longer exists");
            RequireNoChange();
        }

        [Fact]
        public void DirectoryReplacedBetweenChecksIsWatchedAgain()
        {
            var directory = _dir.PathOf("replaced");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "data.json");
            File.WriteAllText(path, "one");
            StartWatcher(path);

            // The file goes first, which is a change. The directory goes after a pause longer than
            // the check interval, so a check has already found it present, and it is replaced by a
            // new one before the next check. The platform reports none of this.
            File.Delete(path);
            RequireChange();
            Thread.Sleep(CheckInterval + CheckInterval);
            Directory.Delete(directory);
            Directory.CreateDirectory(directory);

            // The file in the new directory is reported, and so is a later change to it.
            File.WriteAllText(path, "two");
            RequireChange();
            File.WriteAllText(path, "three");
            RequireChange();
        }

        [Fact]
        public void AcceptsANullLogger()
        {
            var path = Path.Combine(_dir.PathOf("no-such-directory"), "data.json");
            _watcher = new FileDataWatcher(new[] { path }, () => _changes.Add(true), null, CheckInterval);
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
