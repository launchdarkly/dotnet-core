using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    /// <summary>
    /// The observed state of one file, or its absence. Two observations are equal when the file
    /// has the same modification time and size, or the same content where the content decides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A configured path can be a symbolic link. A mounted Kubernetes ConfigMap, for example,
    /// serves each file as a link into a directory that is swapped on update, and the link itself
    /// is never touched. The metadata of the link describes the link, not its target, so an
    /// observation reads the metadata of the final target instead.
    /// </para>
    /// <para>
    /// On a framework without link resolution, a path that is a link is observed by its content.
    /// Reading the file costs more than reading its metadata, so this applies to links only. A
    /// reparse point that is not a link, such as a file kept by a cloud storage provider, is
    /// observed by its own metadata.
    /// </para>
    /// <para>
    /// A file that cannot be examined counts as absent.
    /// </para>
    /// </remarks>
    internal struct FileState : IEquatable<FileState>
    {
        // Resolves the final target of a link, or returns null for a reparse point that is not a
        // link. Null on a framework without link resolution.
#if NET6_0_OR_GREATER
        private static readonly Func<FileInfo, FileSystemInfo> DefaultLinkResolver =
            info => info.ResolveLinkTarget(returnFinalTarget: true);
#else
        private static readonly Func<FileInfo, FileSystemInfo> DefaultLinkResolver = null;
#endif

        internal static readonly FileState Absent = new FileState();

        internal bool Exists { get; }
        internal DateTime LastWriteTimeUtc { get; }
        internal long Length { get; }

        // The hash of the content, when the content decides. Null otherwise.
        private readonly byte[] _contentHash;

        private FileState(DateTime lastWriteTimeUtc, long length, byte[] contentHash)
        {
            Exists = true;
            LastWriteTimeUtc = lastWriteTimeUtc;
            Length = length;
            _contentHash = contentHash;
        }

        /// <summary>
        /// Observes the file at the path.
        /// </summary>
        internal static FileState Observe(string path) => Observe(path, DefaultLinkResolver);

        /// <summary>
        /// Observes the file at the path. With a resolver, a link is observed by the metadata of its
        /// final target. Without one, a link is observed by its content.
        /// </summary>
        internal static FileState Observe(string path, Func<FileInfo, FileSystemInfo> resolveLinkTarget)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return Absent;
                }
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    return new FileState(info.LastWriteTimeUtc, info.Length, null);
                }
                if (resolveLinkTarget != null)
                {
                    var target = resolveLinkTarget(info);
                    if (target == null)
                    {
                        // A reparse point that is not a link. Its own metadata describes it.
                        return new FileState(info.LastWriteTimeUtc, info.Length, null);
                    }
                    // A link whose target does not exist, or whose target is not a file, has no
                    // content to load. It is absent.
                    var targetFile = target as FileInfo;
                    if (targetFile == null || !targetFile.Exists)
                    {
                        return Absent;
                    }
                    return new FileState(targetFile.LastWriteTimeUtc, targetFile.Length, null);
                }
                return new FileState(default(DateTime), 0, HashContent(path));
            }
            catch (Exception)
            {
                return Absent;
            }
        }

        /// <summary>
        /// Observes each of the paths, in order.
        /// </summary>
        internal static FileState[] ObserveAll(IReadOnlyList<string> paths)
        {
            var states = new FileState[paths.Count];
            for (var i = 0; i < paths.Count; i++)
            {
                states[i] = Observe(paths[i]);
            }
            return states;
        }

        /// <summary>
        /// Returns true if any state differs from the state at the same position.
        /// </summary>
        internal static bool AnyChanged(FileState[] previous, FileState[] current)
        {
            for (var i = 0; i < current.Length; i++)
            {
                if (!current[i].Equals(previous[i]))
                {
                    return true;
                }
            }
            return false;
        }

        // Opening the file follows the link, so the hash describes the target's content.
        private static byte[] HashContent(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(stream);
            }
        }

        public bool Equals(FileState other) =>
            Exists == other.Exists && LastWriteTimeUtc == other.LastWriteTimeUtc && Length == other.Length &&
            HashesEqual(_contentHash, other._contentHash);

        public override bool Equals(object obj) => obj is FileState other && Equals(other);

        public override int GetHashCode() =>
            Exists.GetHashCode() ^ LastWriteTimeUtc.GetHashCode() ^ Length.GetHashCode() ^
            (_contentHash == null ? 0 : _contentHash.Length);

        private static bool HashesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
