using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
    /// <para>
    /// A single edit often produces several notifications, and a notification can arrive while the
    /// file is still being written. The watcher reports every notification. Feed it into a
    /// <see cref="FileDataReloader"/>, whose debouncing and failure retry handle both.
    /// </para>
    /// <para>
    /// A notification that names an entry which is not one of the configured files runs the
    /// callback only when the state of a configured file in that directory changed. The state is
    /// read through any symbolic link, as <see cref="FileState"/> describes, so a link swap that
    /// replaces a configured file's target without naming the link, which is how a mounted
    /// ConfigMap is updated, is detected, while a busy sibling file costs no reload.
    /// </para>
    /// <para>
    /// A directory that cannot be watched is logged and attempted again every second, while the
    /// other directories are watched. A watch on a directory that is deleted delivers nothing more,
    /// and some platforms report neither the deletion nor a new directory of the same name. So once
    /// a second the watcher checks that each directory exists and that the state of each configured
    /// file matches its last notification. A directory that no longer exists has its watch dropped
    /// and set up again once it exists. A file whose state changed without a notification is
    /// reported, and its directory's watch is set up again because it can be dead. When a watch is
    /// set up after a failure, the callback runs once so that changes made in the meantime are
    /// picked up.
    /// </para>
    /// </remarks>
    internal sealed class FileDataWatcher : IDisposable
    {
        /// <summary>
        /// The interval of the periodic check of the watched directories, which is also the delay
        /// before a failed watch setup is attempted again.
        /// </summary>
        internal static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(1);

        // The failure key recorded for a directory that does not exist. A setup failure with the
        // same key is a repeat and is not logged again at the same level.
        private const string MissingDirectoryKey = "\0missing";

        // The configured files that share one directory, and the watch on that directory.
        private sealed class WatchedDirectory
        {
            internal readonly string Path;
            internal readonly List<string> Files = new List<string>();
            internal readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal);
            // Null while the directory is not watched.
            internal FileSystemWatcher Watcher;
            // The last observed state of each file, in the order of Files.
            internal FileState[] LastStates;
            // The key of the last reported setup failure, or null after a successful setup.
            internal string LastFailureKey;

            internal WatchedDirectory(string path)
            {
                Path = path;
            }
        }

        private readonly object _lock = new object();
        private readonly List<WatchedDirectory> _directories = new List<WatchedDirectory>();
        private readonly Action _onChange;
        private readonly Logger _log;
        private readonly Timer _timer;
        private int _checking;
        private volatile bool _disposed;

        /// <summary>
        /// Creates a started watcher. A directory that cannot be watched is logged and attempted
        /// again every second. Call Dispose to stop the watcher.
        /// </summary>
        /// <param name="paths">the files to watch</param>
        /// <param name="onChange">invoked for each change notification that concerns one of the files</param>
        /// <param name="log">receives log output about watch failures</param>
        internal FileDataWatcher(IEnumerable<string> paths, Action onChange, Logger log) :
            this(paths, onChange, log, DefaultCheckInterval)
        {
        }

        internal FileDataWatcher(IEnumerable<string> paths, Action onChange, Logger log, TimeSpan checkInterval)
        {
            _onChange = onChange;
            _log = log ?? Logs.None.Logger("");

            // The full path of each file is computed once. Notifications carry the entry name, which
            // is compared to the configured names of the directory.
            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                var directoryPath = Path.GetDirectoryName(fullPath);
                var directory = _directories.Find(d => d.Path == directoryPath);
                if (directory == null)
                {
                    directory = new WatchedDirectory(directoryPath);
                    _directories.Add(directory);
                }
                directory.Files.Add(fullPath);
                directory.Names.Add(Path.GetFileName(fullPath));
            }

            SetUpWatches(isRetry: false);
            _timer = new Timer(OnCheck, null, checkInterval, checkInterval);
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            var toDispose = new List<FileSystemWatcher>();
            lock (_lock)
            {
                foreach (var directory in _directories)
                {
                    if (directory.Watcher != null)
                    {
                        toDispose.Add(directory.Watcher);
                        directory.Watcher = null;
                    }
                }
            }
            // A notification handler that is waiting for the lock must not wait for a watcher that
            // is being disposed, so the watchers are disposed outside the lock.
            foreach (var watcher in toDispose)
            {
                watcher.Dispose();
            }
        }

        // Sets up the watch on each directory that has none. A directory that cannot be watched is
        // attempted again at the next check. When a setup after a failure succeeds, the callback
        // runs once, because the files can have changed while the directory was not watched.
        private void SetUpWatches(bool isRetry)
        {
            var signal = false;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                foreach (var directory in _directories)
                {
                    if (directory.Watcher != null)
                    {
                        continue;
                    }
                    try
                    {
                        directory.Watcher = CreateWatcher(directory);
                    }
                    catch (Exception e)
                    {
                        LogSetupFailure(directory, e);
                        continue;
                    }
                    if (directory.LastFailureKey != null)
                    {
                        _log.Info("Now watching directory {0}", directory.Path);
                        directory.LastFailureKey = null;
                    }
                    // The states are the baseline that a later notification for another entry is
                    // compared to.
                    directory.LastStates = FileState.ObserveAll(directory.Files);
                    if (isRetry)
                    {
                        signal = true;
                    }
                }
            }
            if (signal)
            {
                _onChange();
            }
        }

        private FileSystemWatcher CreateWatcher(WatchedDirectory directory)
        {
            var watcher = new FileSystemWatcher(directory.Path)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName |
                    NotifyFilters.CreationTime,
                IncludeSubdirectories = false
            };
            // The handlers carry the watcher they were attached to, so a notification from a watcher
            // that has been dropped is ignored.
            watcher.Changed += (sender, args) => OnFileEvent(directory, watcher, args.Name);
            watcher.Created += (sender, args) => OnFileEvent(directory, watcher, args.Name);
            watcher.Deleted += (sender, args) => OnFileEvent(directory, watcher, args.Name);
            watcher.Renamed += (sender, args) =>
            {
                OnFileEvent(directory, watcher, args.OldName);
                OnFileEvent(directory, watcher, args.Name);
            };
            watcher.Error += (sender, args) => OnWatcherError(directory, watcher, args.GetException());
            try
            {
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception)
            {
                watcher.Dispose();
                throw;
            }
            return watcher;
        }

        // Called inside the lock. A persistent failure would repeat the same log entry on every
        // attempt. Repeats of an identical failure are demoted to debug level.
        private void LogSetupFailure(WatchedDirectory directory, Exception e)
        {
            var key = Directory.Exists(directory.Path) ? e.Message : MissingDirectoryKey;
            if (key == directory.LastFailureKey)
            {
                _log.Debug("Unable to watch directory {0}: {1}", directory.Path, LogValues.ExceptionSummary(e));
                return;
            }
            directory.LastFailureKey = key;
            LogHelpers.LogException(_log, "Unable to watch directory " + directory.Path + "; will retry", e);
        }

        // A notification from the directory watch. A notification that names a configured file is a
        // change. One that names another entry is a change only when the state of a configured file
        // in the directory differs from the last observation: a configured file that is a symbolic
        // link can have changed with the other entry. One that names no entry is a change.
        private void OnFileEvent(WatchedDirectory directory, FileSystemWatcher watcher, string name)
        {
            // A handler on a watcher thread must never throw. An unhandled exception there ends the
            // process.
            try
            {
                if (_disposed)
                {
                    return;
                }
                var changed = false;
                lock (_lock)
                {
                    if (_disposed || directory.Watcher != watcher)
                    {
                        return;
                    }
                    var current = FileState.ObserveAll(directory.Files);
                    if (name == null || directory.Names.Contains(name))
                    {
                        changed = true;
                    }
                    else
                    {
                        changed = FileState.AnyChanged(directory.LastStates, current);
                    }
                    directory.LastStates = current;
                }
                if (changed)
                {
                    _onChange();
                }
            }
            catch (Exception e)
            {
                LogHelpers.LogException(_log, "Unexpected error while handling a file change notification", e);
            }
        }

        // An error from the file system, such as a full notification buffer, means that changes may
        // have been dropped, or that the watch itself failed. The watch is set up again, and the
        // files are read again.
        private void OnWatcherError(WatchedDirectory directory, FileSystemWatcher watcher, Exception error)
        {
            try
            {
                if (_disposed)
                {
                    return;
                }
                lock (_lock)
                {
                    if (_disposed || directory.Watcher != watcher)
                    {
                        return;
                    }
                    LogHelpers.LogException(_log, "Error from file system watcher for directory " + directory.Path, error);
                    directory.Watcher = null;
                }
                watcher.Dispose();
                SetUpWatches(isRetry: true);
            }
            catch (Exception e)
            {
                LogHelpers.LogException(_log, "Unexpected error while handling a file system watcher error", e);
            }
        }

        // The periodic check. A configured file whose state differs from its last notification
        // changed without one, and the change is reported. A directory that no longer exists has
        // its watch dropped; its files are absent now, which is such a change when their deletion
        // produced no notification. Otherwise the watch of a directory with a changed file is set up
        // again, because a watch that reports nothing for a changed file can be dead, for example
        // when the directory was replaced by a new one of the same name. Finally, every directory
        // without a watch gets one set up.
        private void OnCheck(object state)
        {
            if (_disposed)
            {
                return;
            }
            // Checks can overlap when one takes longer than the interval. One at a time is enough.
            if (Interlocked.Exchange(ref _checking, 1) != 0)
            {
                return;
            }
            try
            {
                var changed = false;
                var toDispose = new List<FileSystemWatcher>();
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    foreach (var directory in _directories)
                    {
                        if (directory.Watcher == null)
                        {
                            continue;
                        }
                        var current = FileState.ObserveAll(directory.Files);
                        var stateChanged = FileState.AnyChanged(directory.LastStates, current);
                        if (stateChanged)
                        {
                            directory.LastStates = current;
                            changed = true;
                        }
                        if (!Directory.Exists(directory.Path))
                        {
                            _log.Warn("Directory {0} no longer exists; its watch is set up again when it appears", directory.Path);
                            toDispose.Add(directory.Watcher);
                            directory.Watcher = null;
                            directory.LastFailureKey = MissingDirectoryKey;
                        }
                        else if (stateChanged)
                        {
                            _log.Debug("A file in directory {0} changed without a notification; setting up the watch again", directory.Path);
                            toDispose.Add(directory.Watcher);
                            directory.Watcher = null;
                        }
                    }
                }
                foreach (var dropped in toDispose)
                {
                    dropped.Dispose();
                }
                if (changed)
                {
                    _onChange();
                }
                SetUpWatches(isRetry: true);
            }
            catch (Exception e)
            {
                LogHelpers.LogException(_log, "Unexpected error while checking file system watches", e);
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        }
    }
}
