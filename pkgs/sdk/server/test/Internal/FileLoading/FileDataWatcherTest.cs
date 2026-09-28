using System;
using System.IO;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataWatcherTest : BaseTest, IDisposable
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(300);

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
            _watcher = new FileDataWatcher(paths, () => _changed.Enqueue(true), TestLogger);
            return _watcher;
        }

        private void RequireChange() => _changed.ExpectValue(TestTimeout);

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
        public void MissingDirectoryThrows()
        {
            var path = Path.Combine(_dir.PathOf("no-such-directory"), "data.json");
            Assert.ThrowsAny<Exception>(() => StartWatcher(path));
        }
    }
}
