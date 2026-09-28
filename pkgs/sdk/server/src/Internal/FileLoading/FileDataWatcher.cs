using System;
using System.Collections.Generic;
using System.IO;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    /// <summary>
    /// Detects changes to a set of files through file system change notifications. It watches the
    /// directory of each file, so a configured file that does not exist yet is picked up when it
    /// appears, and a deleted file is reported.
    /// </summary>
    /// <remarks>
    /// A single edit often produces several notifications, and a notification can arrive while the
    /// file is still being written. The watcher reports every notification. Feed it into a
    /// <see cref="FileDataReloader"/>, whose debouncing and failure retry handle both.
    /// </remarks>
    internal sealed class FileDataWatcher : IDisposable
    {
        private readonly HashSet<string> _fullPaths = new HashSet<string>();
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly Action _onChange;
        private readonly Logger _log;
        private volatile bool _disposed;

        /// <summary>
        /// Creates a started watcher. Throws if a file's directory does not exist or cannot be
        /// watched. Call Dispose to stop it.
        /// </summary>
        /// <param name="paths">the files to watch</param>
        /// <param name="onChange">invoked for each change notification that concerns one of the files</param>
        /// <param name="log">receives log output about notification errors</param>
        internal FileDataWatcher(IEnumerable<string> paths, Action onChange, Logger log)
        {
            _onChange = onChange;
            _log = log;

            var directories = new HashSet<string>();
            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                _fullPaths.Add(fullPath);
                directories.Add(Path.GetDirectoryName(fullPath));
            }

            try
            {
                foreach (var directory in directories)
                {
                    var watcher = new FileSystemWatcher(directory)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName |
                            NotifyFilters.CreationTime,
                        IncludeSubdirectories = false
                    };
                    watcher.Changed += (sender, args) => OnFileEvent(args.FullPath);
                    watcher.Created += (sender, args) => OnFileEvent(args.FullPath);
                    watcher.Deleted += (sender, args) => OnFileEvent(args.FullPath);
                    watcher.Renamed += (sender, args) =>
                    {
                        OnFileEvent(args.OldFullPath);
                        OnFileEvent(args.FullPath);
                    };
                    watcher.Error += OnWatcherError;
                    _watchers.Add(watcher);
                }
                foreach (var watcher in _watchers)
                {
                    watcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }
            _watchers.Clear();
        }

        private void OnFileEvent(string fullPath)
        {
            if (_disposed)
            {
                return;
            }
            if (_fullPaths.Contains(Path.GetFullPath(fullPath)))
            {
                _onChange();
            }
        }

        // An error from the file system, such as a full notification buffer, means that changes may
        // have been dropped. Treat it as a change so the files are read again.
        private void OnWatcherError(object sender, ErrorEventArgs args)
        {
            if (_disposed)
            {
                return;
            }
            LogHelpers.LogException(_log, "Error from file system watcher", args.GetException());
            _onChange();
        }
    }
}
