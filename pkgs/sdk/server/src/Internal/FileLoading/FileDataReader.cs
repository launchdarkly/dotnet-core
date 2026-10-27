using System.IO;
using System.Text;
using LaunchDarkly.Sdk.Server.Integrations;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    /// <summary>
    /// Reads a data file without getting in the way of whoever writes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A data file that is reloaded on change is, by definition, edited while it is watched, and
    /// a reload reads it right after a change. On Windows, a file that is open for reading with
    /// the default sharing mode cannot be opened for writing, replaced by a rename, or deleted
    /// until the read ends, so an editor that saves again, or a tool that replaces the file,
    /// fails while a reload is in progress. This reader opens the file so that all of those are
    /// allowed. A read that overlaps a write can see incomplete content, which fails to parse
    /// and is retried by the reloader.
    /// </para>
    /// <para>
    /// A writer can still hold the file in a way that denies reading. That read fails like any
    /// other, and the reloader retries it after its delay and on the next change, so the reader
    /// does not wait for the writer itself.
    /// </para>
    /// </remarks>
    internal sealed class FileDataReader : FileDataTypes.IFileReader
    {
        private FileDataReader() { }

        internal static readonly FileDataTypes.IFileReader Instance = new FileDataReader();

        /// <summary>
        /// Opens the file for reading while allowing other processes to read, write, rename, and
        /// delete it.
        /// </summary>
        internal static FileStream Open(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096,
                FileOptions.SequentialScan);

        public string ReadAllText(string path)
        {
            using (var stream = Open(path))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
