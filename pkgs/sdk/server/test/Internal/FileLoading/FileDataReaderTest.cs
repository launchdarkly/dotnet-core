using System.IO;
using System.Text;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataReaderTest
    {
        [Fact]
        public void ReadsTheContent()
        {
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                File.WriteAllText(path, "{\"flags\": {}}");
                Assert.Equal("{\"flags\": {}}", FileDataReader.Instance.ReadAllText(path));
            }
        }

        [Fact]
        public void DetectsTheEncodingFromAByteOrderMark()
        {
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                File.WriteAllText(path, "{\"key\": \"caf\u00e9\"}",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                Assert.Equal("{\"key\": \"caf\u00e9\"}", FileDataReader.Instance.ReadAllText(path));
            }
        }

        [Fact]
        public void MissingFileThrowsFileNotFound()
        {
            using (var dir = TempDirectory.Create())
            {
                Assert.Throws<FileNotFoundException>(() =>
                    FileDataReader.Instance.ReadAllText(dir.PathOf("no-such-file.json")));
            }
        }

        [Fact]
        public void AFileBeingReadCanBeWrittenAndDeleted()
        {
            // On Windows, the sharing mode chosen when a file is opened decides what others may do
            // with it until it is closed. The default for reading denies writing and deleting,
            // which fails the editor or tool that updates the file during a reload.
            using (var dir = TempDirectory.Create())
            {
                var path = dir.PathOf("data.json");
                File.WriteAllText(path, "one");
                using (var stream = FileDataReader.Open(path))
                {
                    File.WriteAllText(path, "two");
                    File.Delete(path);
                }
                Assert.False(File.Exists(path));
            }
        }
    }
}
