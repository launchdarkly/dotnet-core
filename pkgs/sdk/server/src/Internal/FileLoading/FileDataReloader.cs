using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Internal.Model;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    /// <summary>
    /// Configuration for <see cref="FileDataReloader"/>.
    /// </summary>
    internal sealed class FileDataReloaderConfig
    {
        /// <summary>
        /// The files to load. The order is significant. It determines which file wins under the
        /// duplicate keys handling.
        /// </summary>
        internal IReadOnlyList<string> Paths { get; set; }

        /// <summary>
        /// What happens when the same key appears in more than one file.
        /// </summary>
        internal FileDataDuplicateKeysHandling DuplicateKeysHandling { get; set; }

        /// <summary>
        /// When true, a configured file that does not exist is treated as a file with no content.
        /// The reload succeeds with the data of the files that exist. When false, a missing file
        /// fails the reload like any other read error.
        /// </summary>
        internal bool SkipMissingPaths { get; set; }

        /// <summary>
        /// Receives log output about reloads and failures.
        /// </summary>
        internal Logger Logger { get; set; }

        /// <summary>
        /// Reads file contents. Defaults to <see cref="FileDataReader"/>, which does not block a
        /// writer of the file while it reads.
        /// </summary>
        internal FileDataTypes.IFileReader FileReader { get; set; }

        /// <summary>
        /// Parses non-JSON content, or null to accept JSON only.
        /// </summary>
        internal Func<string, object> AlternateParser { get; set; }

        /// <summary>
        /// Builds the full flag definition for a <c>flagValues</c> entry.
        /// </summary>
        internal Func<string, LdValue, FeatureFlag> FlagValueExpander { get; set; }

        /// <summary>
        /// Invoked with each successfully merged result. Calls are serialized. An exception from
        /// Apply is a load failure: it is reported through OnError, the result is not remembered as
        /// applied, and the reload is retried. Apply and OnError must not call back into Dispose.
        /// </summary>
        internal Action<FileDataMergeResult> Apply { get; set; }

        /// <summary>
        /// Invoked when a reload fails, once per distinct failure. With automatic retries, repeats
        /// of an identical failure do not invoke it again. A success re-arms it. The exception is a
        /// <see cref="FileDataReadException"/> when a file could not be read or parsed, or a
        /// <see cref="FileDataException"/> when the documents could not be combined or Apply threw.
        /// The reloader logs failures itself. An exception from OnError is logged and does not stop
        /// the retry.
        /// </summary>
        internal Action<Exception> OnError { get; set; }

        /// <summary>
        /// How long to wait after a Trigger call for further calls to settle before reloading. This
        /// coalesces bursts of change notifications into one reload. If zero or negative, each
        /// Trigger reloads immediately on a worker thread.
        /// </summary>
        internal TimeSpan DebounceDelay { get; set; }

        /// <summary>
        /// The longest that a stream of Trigger calls which never settles can postpone a reload,
        /// measured from the first call of the stream. A file that is written continuously, or a
        /// directory with constant activity, still reloads at this interval. Defaults to
        /// <see cref="FileDataReloader.DefaultMaxDebounceDelay"/>; zero or negative means no bound.
        /// Ignored when DebounceDelay is zero or negative.
        /// </summary>
        internal TimeSpan MaxDebounceDelay { get; set; } = FileDataReloader.DefaultMaxDebounceDelay;

        /// <summary>
        /// How long to wait after a failed reload before retrying, so that a failure observed while
        /// a file was being rewritten recovers even if no further change notification arrives. If
        /// zero or negative, there is no automatic retry.
        /// </summary>
        internal TimeSpan RetryDelay { get; set; }

        /// <summary>
        /// If true, the Apply call is skipped when the files' raw contents are identical to the
        /// last successfully applied contents.
        /// </summary>
        internal bool SkipUnchanged { get; set; }
    }

    /// <summary>
    /// Owns the reload cycle for a set of data files. It serializes reloads, debounces change
    /// signals, retains the last good result on failure by not calling Apply, retries after
    /// failures, and skips no-op applications.
    /// </summary>
    internal sealed class FileDataReloader : IDisposable
    {
        /// <summary>
        /// A settle window long enough to coalesce the burst of change notifications produced by a
        /// single file edit, and short enough to stay responsive.
        /// </summary>
        internal static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// The bound on how long settling can postpone a reload. A second of stale data is
        /// acceptable; postponing without end while a file is written continuously is not.
        /// </summary>
        internal static readonly TimeSpan DefaultMaxDebounceDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Bounds how long a failed reload can go uncorrected when no further change notification
        /// arrives, for example when the failure came from reading a file mid-write. Reading a
        /// local file is cheap, so this can be short.
        /// </summary>
        internal static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

        private readonly FileDataReloaderConfig _config;
        private readonly FileDataParser _parser;
        private readonly FileDataTypes.IFileReader _fileReader;
        private readonly Logger _log;

        // Serializes the load work between ReloadNow and the timer callbacks.
        private readonly object _reloadLock = new object();
        private byte[] _lastGoodHash; // only touched inside _reloadLock
        private string _lastErrorMessage; // only touched inside _reloadLock

        // Guards the timers. The timers are created on first use so that a reloader that is never
        // used holds no scheduled work.
        private readonly object _timerLock = new object();
        private Timer _debounceTimer;
        // Stopwatch timestamp of the first trigger of the pending stream, or NoBurst when no reload is
        // pending. Guarded by _timerLock.
        private long _burstStartTimestamp = NoBurst;
        private const long NoBurst = -1;
        private Timer _retryTimer;
        private bool _retryArmed;
        private int _immediateReloadPending;

        // Guards the state of triggered reloads. One thread at a time performs them; a request that
        // arrives while one runs is remembered and served by one follow-up reload.
        private readonly object _pendingLock = new object();
        private bool _reloading;
        private bool _reloadPending;

        private volatile bool _disposed;

        internal FileDataReloader(FileDataReloaderConfig config)
        {
            _config = config;
            _parser = new FileDataParser(config.AlternateParser);
            _fileReader = config.FileReader ?? FileDataReader.Instance;
            _log = config.Logger ?? Logs.None.Logger("");
        }

        /// <summary>
        /// Synchronously loads the files and applies the result, or reports the failure. Use it
        /// for the initial load. A failure here arms the same automatic retry as a failed
        /// triggered reload, and a success disarms a pending retry.
        /// </summary>
        internal void ReloadNow()
        {
            if (_disposed)
            {
                return;
            }
            if (Reload())
            {
                // A retry armed by an earlier failure is redundant after this success.
                DisarmRetry();
            }
            else
            {
                // An already armed retry keeps its earlier deadline.
                ArmRetry(keepExistingDeadline: true);
            }
        }

        /// <summary>
        /// Signals that the files may have changed and a reload should happen after the debounce
        /// delay. It never blocks. Signals that arrive while a reload is already pending are
        /// coalesced, and signals that arrive while a reload is running are coalesced into one
        /// follow-up reload.
        /// </summary>
        internal void Trigger()
        {
            if (_disposed)
            {
                return;
            }
            if (_config.DebounceDelay <= TimeSpan.Zero)
            {
                if (Interlocked.Exchange(ref _immediateReloadPending, 1) == 0)
                {
                    Task.Run(() => RunGuarded(() =>
                    {
                        Interlocked.Exchange(ref _immediateReloadPending, 0);
                        RequestReload(isRetry: false);
                    }));
                }
                return;
            }
            lock (_timerLock)
            {
                if (_disposed)
                {
                    return;
                }
                if (_debounceTimer == null)
                {
                    _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
                }
                // Each trigger moves the deadline out again. The reload runs after activity settles,
                // or when the stream of triggers has postponed it for as long as the bound allows.
                var now = Stopwatch.GetTimestamp();
                if (_burstStartTimestamp == NoBurst)
                {
                    _burstStartTimestamp = now;
                }
                var delay = _config.DebounceDelay;
                if (_config.MaxDebounceDelay > TimeSpan.Zero)
                {
                    var elapsed = TimeSpan.FromTicks((now - _burstStartTimestamp) * TimeSpan.TicksPerSecond /
                        Stopwatch.Frequency);
                    var remaining = _config.MaxDebounceDelay - elapsed;
                    if (remaining < delay)
                    {
                        delay = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
                    }
                }
                _debounceTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Stops the reloader. It does not wait for a reload that is already in progress. A reload
        /// wedged in a blocking file read must not be able to wedge shutdown. Such a reload can
        /// still deliver its result through Apply or OnError shortly after Dispose returns. A
        /// reload that has not yet reached its callbacks when Dispose is called does not invoke
        /// them.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            lock (_timerLock)
            {
                _debounceTimer?.Dispose();
                _retryTimer?.Dispose();
                _debounceTimer = null;
                _retryTimer = null;
                _retryArmed = false;
            }
        }

        private void OnDebounceElapsed(object state)
        {
            lock (_timerLock)
            {
                // The next trigger starts a new stream, with a fresh bound.
                _burstStartTimestamp = NoBurst;
            }
            RunGuarded(() => RequestReload(isRetry: false));
        }

        private void OnRetryElapsed(object state) => RunGuarded(() => RequestReload(isRetry: true));

        // Timer callbacks and worker tasks run on thread pool threads. An unhandled exception there
        // ends the process, so every callback logs instead.
        private void RunGuarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                LogHelpers.LogException(_log, "Unexpected error while reloading file data", e);
            }
        }

        // Performs a triggered reload, or remembers the request when a reload is already running. The
        // running thread performs one more reload when it finishes. All the requests that arrive
        // during one reload describe the same state of the files, so one follow-up reload serves
        // them all, and no thread waits for the reload lock. A retry that arrives while a reload
        // runs is dropped: that reload decides whether a retry is needed.
        private void RequestReload(bool isRetry)
        {
            lock (_pendingLock)
            {
                if (_reloading)
                {
                    if (!isRetry)
                    {
                        _reloadPending = true;
                    }
                    return;
                }
                _reloading = true;
            }
            var completed = false;
            try
            {
                var retry = isRetry;
                do
                {
                    ReloadAfterSignal(retry);
                    retry = false;
                } while (TakePendingReload());
                completed = true;
            }
            finally
            {
                // An unexpected exception must not leave the gate closed forever.
                if (!completed)
                {
                    lock (_pendingLock)
                    {
                        _reloading = false;
                        _reloadPending = false;
                    }
                }
            }
        }

        // Called by the reloading thread after each reload. Returns true when a request arrived
        // during the reload. Otherwise it records that no reload runs and returns false.
        private bool TakePendingReload()
        {
            lock (_pendingLock)
            {
                if (_reloadPending)
                {
                    _reloadPending = false;
                    return true;
                }
                _reloading = false;
                return false;
            }
        }

        private void ReloadAfterSignal(bool isRetry)
        {
            if (_disposed)
            {
                return;
            }
            if (isRetry)
            {
                _log.Debug("Retrying file data load after earlier failure");
            }
            else
            {
                _log.Info("Reloading file data after detecting a change");
            }
            // A pending retry is superseded by this reload. It either succeeds, or it fails and
            // arms a fresh retry.
            DisarmRetry();
            if (!Reload())
            {
                ArmRetry(keepExistingDeadline: false);
            }
        }

        private void ArmRetry(bool keepExistingDeadline)
        {
            if (_config.RetryDelay <= TimeSpan.Zero)
            {
                return;
            }
            lock (_timerLock)
            {
                if (_disposed)
                {
                    return;
                }
                if (_retryArmed && keepExistingDeadline)
                {
                    return;
                }
                if (_retryTimer == null)
                {
                    _retryTimer = new Timer(OnRetryElapsed, null, Timeout.Infinite, Timeout.Infinite);
                }
                _retryArmed = true;
                _retryTimer.Change(_config.RetryDelay, Timeout.InfiniteTimeSpan);
            }
        }

        private void DisarmRetry()
        {
            lock (_timerLock)
            {
                _retryArmed = false;
                _retryTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        // Performs one full load of all configured files. Returns true if the load succeeded, which
        // decides whether a retry gets armed. A skipped no-op application counts as success. The
        // whole set is re-read on every reload: entries are combined across files in order, so a
        // change to one file can alter which file wins for a key.
        private bool Reload()
        {
            lock (_reloadLock)
            {
                // A trigger already queued when Dispose was called can still reach here.
                if (_disposed)
                {
                    return true;
                }

                var documents = new List<FileDataDocument>(_config.Paths.Count);
                var files = new List<FileDataFileSummary>(_config.Paths.Count);
                var rawContents = _config.SkipUnchanged ? new StringBuilder() : null;
                foreach (var path in _config.Paths)
                {
                    string content;
                    try
                    {
                        content = _fileReader.ReadAllText(path);
                    }
                    catch (FileNotFoundException) when (_config.SkipMissingPaths)
                    {
                        _log.Debug("File {0} does not exist; it contributes no data", path);
                        files.Add(new FileDataFileSummary(path, false, 0, 0));
                        continue;
                    }
                    catch (DirectoryNotFoundException) when (_config.SkipMissingPaths)
                    {
                        _log.Debug("File {0} does not exist; it contributes no data", path);
                        files.Add(new FileDataFileSummary(path, false, 0, 0));
                        continue;
                    }
                    catch (Exception e)
                    {
                        return Fail(new FileDataReadException(path, "unable to read file: " + e.Message, e));
                    }
                    // One read feeds both the hash and the parse, so the skip-unchanged hash can never
                    // disagree with the content that was actually applied.
                    rawContents?.Append(content).Append('\0');
                    FileDataDocument document;
                    try
                    {
                        document = _parser.Parse(content);
                    }
                    catch (Exception e)
                    {
                        return Fail(new FileDataReadException(path, "error parsing file: " + e.Message, e));
                    }
                    documents.Add(document);
                    files.Add(new FileDataFileSummary(path, true, 0, 0));
                }

                FileDataMergeResult merged;
                try
                {
                    merged = FileDataMerger.Merge(_config.DuplicateKeysHandling, documents, _config.FlagValueExpander);
                }
                catch (FileDataException e)
                {
                    return Fail(e);
                }
                catch (Exception e)
                {
                    // The flag value expander is supplied by the consumer. Its failure is a failure
                    // to process the files, like a merge failure.
                    return Fail(new FileDataException("error expanding flag values: " + e.Message, e));
                }

                // Documents are the present files in order. Copy their counts onto the file summaries.
                var filesWithCounts = ImmutableList.CreateBuilder<FileDataFileSummary>();
                var next = 0;
                foreach (var file in files)
                {
                    if (file.Present)
                    {
                        var summary = merged.Documents[next++];
                        filesWithCounts.Add(new FileDataFileSummary(file.Path, true, summary.Flags, summary.Segments));
                    }
                    else
                    {
                        filesWithCounts.Add(file);
                    }
                }

                // Dispose may have happened while the files were being read. Deliver nothing in that
                // case. This check is deliberately not atomic with the delivery below. Dispose never
                // blocks on a lock shared with callbacks, so a reload that passes this check can rarely
                // deliver just after Dispose returns.
                if (_disposed)
                {
                    return true;
                }

                // A success right after a failure must apply even when the content is unchanged since
                // the last success. The consumer heard about the failure through OnError and only
                // Apply tells it things are good again.
                var recovering = _lastErrorMessage != null;
                byte[] hash = null;
                if (rawContents != null)
                {
                    hash = ComputeHash(rawContents.ToString());
                    if (!recovering && HashesEqual(hash, _lastGoodHash))
                    {
                        return true;
                    }
                }
                // Nothing is remembered until the consumer has accepted the result. A result the
                // consumer rejects must not become the baseline that skip-unchanged compares
                // against, and must not count as a recovery. The rejection is a load failure.
                try
                {
                    _config.Apply?.Invoke(merged.WithFiles(filesWithCounts.ToImmutable()));
                }
                catch (Exception e)
                {
                    return Fail(new FileDataException("error applying file data: " + e.Message, e));
                }
                _lastErrorMessage = null;
                _lastGoodHash = hash;
                return true;
            }
        }

        // Called inside _reloadLock.
        private bool Fail(Exception e)
        {
            // Dispose may have happened while the files were being read. Deliver nothing in that
            // case, and report success so no retry is armed.
            if (_disposed)
            {
                return true;
            }
            // With automatic retries, a persistent failure would repeat the same log entry and the
            // same callback on every attempt. Repeats of an identical failure are demoted to debug
            // level and do not invoke OnError again.
            if (e.Message == _lastErrorMessage)
            {
                _log.Debug("Unable to load flags: {0}", e.Message);
                return false;
            }
            _lastErrorMessage = e.Message;
            _log.Error("Unable to load flags: {0}", e.Message);
            try
            {
                _config.OnError?.Invoke(e);
            }
            catch (Exception callbackError)
            {
                // A consumer that throws while it is told about a failure must not stop the retry.
                LogHelpers.LogException(_log, "Error while reporting a file data load failure", callbackError);
            }
            return false;
        }

        private static byte[] ComputeHash(string contents)
        {
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(contents));
            }
        }

        private static bool HashesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
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
