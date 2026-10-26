using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class FileDataSourceBuilderTest
    {
        private readonly BuilderBehavior.InternalStateTester<FileDataSourceBuilder> _tester =
            BuilderBehavior.For(FileData.DataSource);

        [Fact]
        public void AutoUpdate()
        {
            var prop = _tester.Property(b => b._autoUpdate, (b, v) => b.AutoUpdate(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
        }

        [Fact]
        public void DuplicateKeysHandling()
        {
            var prop = _tester.Property(b => b._duplicateKeysHandling, (b, v) => b.DuplicateKeysHandling(v));
            prop.AssertDefault(FileDataTypes.DuplicateKeysHandling.Throw);
            prop.AssertCanSet(FileDataTypes.DuplicateKeysHandling.Ignore);
        }

        [Fact]
        public void FilePaths()
        {
            Assert.Empty(FileData.DataSource()._paths);

            var fd = FileData.DataSource();
            fd.FilePaths("path1");
            fd.FilePaths("path2", "path3");
            Assert.Equal(new List<string> { "path1", "path2", "path3" }, fd._paths);
        }

        [Fact]
        public void FileReader()
        {
            var prop = _tester.Property(b => b._fileReader, (b, v) => b.FileReader(v));
            prop.AssertDefault(FlagFileReader.Instance);
            prop.AssertCanSet(new TestFileReader());
        }

        [Fact]
        public void Parser()
        {
            Assert.Empty(FileData.DataSource()._parsers);

            Func<string, object> p = s => s;
            Func<string, object> q = s => null;
            var b = FileData.DataSource().Parser("YAML", p).Parser(".yml", p).Parser(".yaml", q);
            Assert.Equal(new[] { ".yaml", ".yml" }, b._parsers.Keys.OrderBy(k => k));
            Assert.Same(q, b._parsers[".yaml"]);
            Assert.Same(p, b._parsers[".yml"]);

            Assert.Throws<ArgumentException>(() => FileData.DataSource().Parser("", p));
            Assert.Throws<ArgumentException>(() => FileData.DataSource().Parser(null, p));
            Assert.Throws<ArgumentNullException>(() => FileData.DataSource().Parser(".yaml", null));
        }

        [Fact]
        public void SkipMissingPaths()
        {
            var prop = _tester.Property(b => b._skipMissingPaths, (b, v) => b.SkipMissingPaths(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
            Assert.False(FileData.DataSource()._skipMissingPaths);
        }

        private class TestFileReader : FileDataTypes.IFileReader
        {
            public string ReadAllText(string path) => null;
        }
    }
}
