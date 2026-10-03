using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileStateTest : BaseTest, IDisposable
    {
        private readonly TempDirectory _dir = TempDirectory.Create();

        public FileStateTest(ITestOutputHelper testOutput) : base(testOutput) { }

        public void Dispose() => _dir.Dispose();

        private static void WriteWithModTime(string path, string content, DateTime modTime)
        {
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, modTime);
        }

        [Fact]
        public void MissingFileIsAbsent()
        {
            var state = FileState.Observe(_dir.PathOf("missing.json"));
            Assert.False(state.Exists);
            Assert.Equal(FileState.Absent, state);
        }

        [Fact]
        public void RegularFileIsObservedByModificationTimeAndSize()
        {
            var path = _dir.PathOf("data.json");
            var modTime = DateTime.UtcNow.AddHours(-1);
            WriteWithModTime(path, "one", modTime);

            var state = FileState.Observe(path);

            Assert.True(state.Exists);
            Assert.Equal(modTime, state.LastWriteTimeUtc);
            Assert.Equal(3, state.Length);
            // The same metadata is the same state. Changed metadata is a different state.
            Assert.Equal(state, FileState.Observe(path));
            WriteWithModTime(path, "two!", modTime);
            Assert.NotEqual(state, FileState.Observe(path));
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void LinkIsObservedByItsTargetMetadata()
        {
            var target = _dir.PathOf("target.json");
            var modTime = DateTime.UtcNow.AddHours(-1);
            WriteWithModTime(target, "one", modTime);
            var link = _dir.PathOf("link.json");
            if (!SymbolicLinks.TryCreateFileLink(link, target))
            {
                TestLogger.Info("symbolic links cannot be created in this environment; skipping");
                return;
            }

            var state = FileState.Observe(link);

            // The link's own metadata describes the link. The observation describes the target.
            Assert.True(state.Exists);
            Assert.Equal(modTime, state.LastWriteTimeUtc);
            Assert.Equal(3, state.Length);
            Assert.Equal(FileState.Observe(target), state);
        }

        [Fact]
        public void LinkWithoutATargetIsAbsent()
        {
            var link = _dir.PathOf("link.json");
            if (!SymbolicLinks.TryCreateFileLink(link, _dir.PathOf("missing.json")))
            {
                TestLogger.Info("symbolic links cannot be created in this environment; skipping");
                return;
            }

            Assert.False(FileState.Observe(link).Exists);
        }

        [Fact]
        public void LinkIsObservedByContentWhenLinksAreNotResolved()
        {
            var target = _dir.PathOf("target.json");
            WriteWithModTime(target, "one", DateTime.UtcNow.AddHours(-1));
            var link = _dir.PathOf("link.json");
            if (!SymbolicLinks.TryCreateFileLink(link, target))
            {
                TestLogger.Info("symbolic links cannot be created in this environment; skipping");
                return;
            }

            var state = FileState.Observe(link, resolveLinks: false);

            // A rewrite with the same content is the same state. A rewrite with other content, even
            // of the same size and modification time, is a different state.
            Assert.True(state.Exists);
            WriteWithModTime(target, "one", DateTime.UtcNow.AddHours(-2));
            Assert.Equal(state, FileState.Observe(link, resolveLinks: false));
            WriteWithModTime(target, "two", DateTime.UtcNow.AddHours(-2));
            Assert.NotEqual(state, FileState.Observe(link, resolveLinks: false));
        }

        [Fact]
        public void RegularFileIsObservedByMetadataWhenLinksAreNotResolved()
        {
            var path = _dir.PathOf("data.json");
            var modTime = DateTime.UtcNow.AddHours(-1);
            WriteWithModTime(path, "one", modTime);

            var state = FileState.Observe(path, resolveLinks: false);

            Assert.Equal(modTime, state.LastWriteTimeUtc);
            Assert.Equal(3, state.Length);
        }
#endif
    }
}
