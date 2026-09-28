using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    /// <summary>
    /// Detects changes to a set of files by examining them on a fixed interval. Use it where file
    /// system change notifications are not available or not reliable. A change to the modification
    /// time or the size of any file invokes the change callback. A file that appears or disappears is
    /// also a change. A file that cannot be examined counts as absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The poller samples the files once per interval and compares only modification time and size.
    /// A rewrite that keeps both values is not detected.
    /// </para>
    /// <para>
    /// Detection is generous. The callback can run for a change that does not alter the effective
    /// data. Feed it into a <see cref="FileDataReloader"/>, whose debouncing and skip-unchanged
    /// handling absorb the excess.
    /// </para>
    /// </remarks>
    internal sealed class FileDataPoller : IDisposable
    {
        private struct FileState : IEquatable<FileState>
        {
            internal bool Exists;
            internal DateTime LastWriteTimeUtc;
            internal long Length;

            public bool Equals(FileState other) =>
                Exists == other.Exists && LastWriteTimeUtc == other.LastWriteTimeUtc && Length == other.Length;

            public override bool Equals(object obj) => obj is FileState other && Equals(other);

            public override int GetHashCode() =>
                Exists.GetHashCode() ^ LastWriteTimeUtc.GetHashCode() ^ Length.GetHashCode();
        }

        private readonly string[] _paths;
        private readonly Action _onChange;
        private readonly Logger _log;
        private readonly Timer _timer;
        private FileState[] _last;
        private int _examining;
        private volatile bool _disposed;

        /// <summary>
        /// Creates a started poller. It examines the files once before it returns, so only later
        /// changes invoke the callback. Call Dispose to stop it.
        /// </summary>
        /// <param name="paths">the files to examine</param>
        /// <param name="interval">the time between examinations</param>
        /// <param name="onChange">invoked when a change is detected</param>
        /// <param name="log">receives log output about unexpected errors</param>
        internal FileDataPoller(IEnumerable<string> paths, TimeSpan interval, Action onChange, Logger log)
        {
            _paths = paths.ToArray();
            _onChange = onChange;
            _log = log;
            _last = ObserveAll(_paths);
            _timer = new Timer(OnTick, null, interval, interval);
        }

        /// <summary>
        /// Stops the poller. It does not wait for an examination or a callback that is in progress.
        /// A file system that does not respond must not block shutdown. As a result, the callback
        /// can run one more time shortly after Dispose returns. Consumers tolerate a late call, as
        /// they do for a late reload.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }

        private void OnTick(object state)
        {
            if (_disposed)
            {
                return;
            }
            // Timer callbacks can overlap when an examination or the callback runs longer than the
            // interval. One examination at a time is enough.
            if (Interlocked.Exchange(ref _examining, 1) != 0)
            {
                return;
            }
            try
            {
                var current = ObserveAll(_paths);
                var changed = false;
                for (var i = 0; i < current.Length; i++)
                {
                    if (!current[i].Equals(_last[i]))
                    {
                        changed = true;
                        break;
                    }
                }
                _last = current;
                if (changed && !_disposed)
                {
                    _onChange();
                }
            }
            catch (Exception e)
            {
                LogHelpers.LogException(_log, "Unexpected error while examining files for changes", e);
            }
            finally
            {
                Interlocked.Exchange(ref _examining, 0);
            }
        }

        private static FileState[] ObserveAll(string[] paths)
        {
            var states = new FileState[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                var info = new FileInfo(paths[i]);
                if (info.Exists)
                {
                    states[i] = new FileState
                    {
                        Exists = true,
                        LastWriteTimeUtc = info.LastWriteTimeUtc,
                        Length = info.Length
                    };
                }
            }
            return states;
        }
    }
}
