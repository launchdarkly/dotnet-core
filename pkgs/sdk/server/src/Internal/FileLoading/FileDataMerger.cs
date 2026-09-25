using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Server.Internal.Model;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    /// <summary>
    /// Determines what happens when the same flag or segment key appears in more than one document.
    /// </summary>
    internal enum FileDataDuplicateKeysHandling
    {
        /// <summary>
        /// A duplicated key makes the merge fail.
        /// </summary>
        Fail,

        /// <summary>
        /// Only the first occurrence of a duplicated key is used, in the order the documents were given.
        /// </summary>
        Ignore
    }

    /// <summary>
    /// An error in loading or combining file data. The message describes the problem.
    /// </summary>
    internal class FileDataException : Exception
    {
        internal FileDataException(string message) : base(message) { }

        internal FileDataException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Indicates that one of the source files could not be read or parsed. It distinguishes a
    /// per-file failure from a failure to merge the files' contents.
    /// </summary>
    internal sealed class FileDataReadException : FileDataException
    {
        /// <summary>
        /// The path of the file that failed.
        /// </summary>
        internal string Path { get; }

        internal FileDataReadException(string path, string description, Exception innerException) :
            base(description + " [" + path + "]", innerException)
        {
            Path = path;
        }
    }

    /// <summary>
    /// Counts the entries the merge kept from one document. An entry dropped by the duplicate keys
    /// handling is not counted.
    /// </summary>
    internal struct FileDataDocumentSummary
    {
        internal int Flags { get; }
        internal int Segments { get; }

        internal FileDataDocumentSummary(int flags, int segments)
        {
            Flags = flags;
            Segments = segments;
        }
    }

    /// <summary>
    /// Describes one configured file after a reload.
    /// </summary>
    internal struct FileDataFileSummary
    {
        internal string Path { get; }

        /// <summary>
        /// False when the file does not exist and missing files are skipped.
        /// </summary>
        internal bool Present { get; }

        internal int Flags { get; }
        internal int Segments { get; }

        internal FileDataFileSummary(string path, bool present, int flags, int segments)
        {
            Path = path;
            Present = present;
            Flags = flags;
            Segments = segments;
        }
    }

    /// <summary>
    /// The merged items from one or more documents. Item values are <see cref="FeatureFlag"/> or
    /// <see cref="Segment"/>.
    /// </summary>
    /// <remarks>
    /// All of one document's items precede the next document's, in the order the documents were
    /// given. Within one document, the file order is kept.
    /// </remarks>
    internal sealed class FileDataMergeResult
    {
        internal IReadOnlyList<KeyValuePair<string, ItemDescriptor>> Flags { get; }
        internal IReadOnlyList<KeyValuePair<string, ItemDescriptor>> Segments { get; }

        /// <summary>
        /// For each input document in order, the number of entries the merge kept from it.
        /// </summary>
        internal IReadOnlyList<FileDataDocumentSummary> Documents { get; }

        /// <summary>
        /// Set by the reloader. Describes each configured file in order.
        /// </summary>
        internal IReadOnlyList<FileDataFileSummary> Files { get; }

        internal FileDataMergeResult(
            IReadOnlyList<KeyValuePair<string, ItemDescriptor>> flags,
            IReadOnlyList<KeyValuePair<string, ItemDescriptor>> segments,
            IReadOnlyList<FileDataDocumentSummary> documents,
            IReadOnlyList<FileDataFileSummary> files
            )
        {
            Flags = flags;
            Segments = segments;
            Documents = documents;
            Files = files ?? ImmutableList<FileDataFileSummary>.Empty;
        }

        internal FileDataMergeResult WithFiles(IReadOnlyList<FileDataFileSummary> files) =>
            new FileDataMergeResult(Flags, Segments, Documents, files);
    }

    /// <summary>
    /// Combines the items of several documents into one set.
    /// </summary>
    internal static class FileDataMerger
    {
        /// <summary>
        /// Combines the items of the given documents, expanding flag-value entries into full flag
        /// definitions and applying the given duplicate keys handling. An unrecognized handling
        /// value behaves as <see cref="FileDataDuplicateKeysHandling.Fail"/>.
        /// </summary>
        /// <param name="duplicateKeysHandling">what to do when a key appears more than once</param>
        /// <param name="documents">the documents, in precedence order</param>
        /// <param name="expandFlagValue">builds the full flag definition for a flag-value entry</param>
        /// <returns>the merged result</returns>
        /// <exception cref="FileDataException">a key appears more than once and the handling is Fail</exception>
        internal static FileDataMergeResult Merge(
            FileDataDuplicateKeysHandling duplicateKeysHandling,
            IReadOnlyList<FileDataDocument> documents,
            Func<string, LdValue, FeatureFlag> expandFlagValue
            )
        {
            var flags = ImmutableList.CreateBuilder<KeyValuePair<string, ItemDescriptor>>();
            var segments = ImmutableList.CreateBuilder<KeyValuePair<string, ItemDescriptor>>();
            var summaries = ImmutableList.CreateBuilder<FileDataDocumentSummary>();
            var seenFlagKeys = new HashSet<string>();
            var seenSegmentKeys = new HashSet<string>();

            foreach (var document in documents)
            {
                var flagCount = 0;
                var segmentCount = 0;
                foreach (var kv in document.Flags)
                {
                    if (Insert(flags, seenFlagKeys, "flag", kv.Key, kv.Value.Version, kv.Value, duplicateKeysHandling))
                    {
                        flagCount++;
                    }
                }
                foreach (var kv in document.FlagValues)
                {
                    var flag = expandFlagValue(kv.Key, kv.Value);
                    if (Insert(flags, seenFlagKeys, "flag", kv.Key, flag.Version, flag, duplicateKeysHandling))
                    {
                        flagCount++;
                    }
                }
                foreach (var kv in document.Segments)
                {
                    if (Insert(segments, seenSegmentKeys, "segment", kv.Key, kv.Value.Version, kv.Value, duplicateKeysHandling))
                    {
                        segmentCount++;
                    }
                }
                summaries.Add(new FileDataDocumentSummary(flagCount, segmentCount));
            }

            return new FileDataMergeResult(flags.ToImmutable(), segments.ToImmutable(), summaries.ToImmutable(), null);
        }

        // Adds the entry unless the key was already seen. Returns true if it added the entry.
        private static bool Insert(
            ImmutableList<KeyValuePair<string, ItemDescriptor>>.Builder items,
            ISet<string> seenKeys,
            string category,
            string key,
            int version,
            object item,
            FileDataDuplicateKeysHandling duplicateKeysHandling
            )
        {
            if (seenKeys.Contains(key))
            {
                switch (duplicateKeysHandling)
                {
                    case FileDataDuplicateKeysHandling.Ignore:
                        return false;
                    default:
                        throw new FileDataException(category + " \"" + key + "\" is specified by multiple files");
                }
            }
            items.Add(new KeyValuePair<string, ItemDescriptor>(key, new ItemDescriptor(version, item)));
            seenKeys.Add(key);
            return true;
        }
    }
}
