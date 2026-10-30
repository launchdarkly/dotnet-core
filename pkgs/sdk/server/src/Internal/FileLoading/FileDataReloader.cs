using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
        /// When true, a configured path whose directory does not exist fails the reload even when
        /// SkipMissingPaths is set, which then only skips a file that is missing from a directory
        /// that exists. A directory that disappears keeps the last good data in place instead of
        /// applying the files that remain. Ignored when SkipMissingPaths is false.
        /// </summary>
        internal bool FailOnMissingDirectory { get; set; }

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
        /// Parsers for file formats other than JSON, by file extension. A file whose extension has
        /// no entry is parsed as JSON. Null means JSON only.
        /// </summary>
        internal IReadOnlyDictionary<string, Func<string, object>> ParsersByExtension { get; set; }

        /// <summary>
        /// Builds the full flag definition for a <c>flagValues</c> entry.
        /// </summary>
        internal Func<string, LdValue, FeatureFlag> FlagValueExpander { get; set; }

        /// <summary>
        /// Invoked with each successfully merged result. Calls are serialized. An exception from
        /// Apply is a load failure: it is logged, the result is not remembered as applied, and the
        /// reload is retried. Apply must not call back into Dispose.
        /// </summary>
        internal Action<FileDataMergeResult> Apply { get; set; }

        /// <summary>
        /// If true, a failed reload is retried after <see cref="FileDataReloaderTiming.RetryDelay"/>,
        /// so that a failure observed while a file was being rewritten recovers even if no further
        /// change notification arrives. If false, a failed reload waits for the next trigger.
        /// </summary>
        internal bool RetryOnFailure { get; set; } = true;

        /// <summary>
        /// The number of consecutive failed load attempts after which the reloader gives up
        /// retrying until the next Trigger or ReloadNow call. A reload that is not a retry starts
        /// a new run of attempts and is counted as the first; a success ends the run. When the
        /// bound is reached, no retry is armed and one error is logged. Zero, the default, means
        /// no bound: a failing load is retried until it succeeds. The bound has an effect only
        /// when RetryOnFailure is true.
        /// </summary>
        internal int MaxLoadAttempts { get; set; }

        /// <summary>
        /// If true, the Apply call is skipped when every file's presence and raw content are
        /// identical to those of the last successfully applied load.
        /// </summary>
        internal bool SkipUnchanged { get; set; }
    }

    /// <summary>
    /// The delays of the reload cycle. The SDK uses <see cref="Production"/>. Tests pass shorter
    /// delays so that they do not wait for the production ones; nothing else sets them.
    /// </summary>
    internal sealed class FileDataReloaderTiming
    {
        /// <summary>
        /// The production delays. The settle window is 100 milliseconds: long enough to coalesce
        /// the burst of change notifications produced by a single file edit, and short enough to
        /// stay responsive. Settling can postpone a reload by at most one second: a second of stale
        /// data is acceptable, and postponing without end while a file is written continuously is
        /// not. A failed reload is retried after one second, which bounds how long a failure can go
        /// uncorrected when no further change notification arrives; reading a local file is cheap,
        /// so this can be short.
        /// </summary>
        internal static readonly FileDataReloaderTiming Production = new FileDataReloaderTiming(
            TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        /// <summary>
        /// How long to wait after a Trigger call for further calls to settle before reloading. This
        /// coalesces bursts of change notifications into one reload.
        /// </summary>
        internal TimeSpan DebounceDelay { get; }

        /// <summary>
        /// The longest that a stream of Trigger calls which never settles can postpone a reload,
        /// measured from the first call of the stream.
        /// </summary>
        internal TimeSpan MaxDebounceDelay { get; }

        /// <summary>
        /// How long to wait after a failed reload before retrying it.
        /// </summary>
        internal TimeSpan RetryDelay { get; }

        /// <summary>
        /// Constructs a timing.
        /// </summary>
        /// <exception cref="ArgumentException">a delay is not positive, or the settle window is
        /// longer than the bound on postponing</exception>
        internal FileDataReloaderTiming(TimeSpan debounceDelay, TimeSpan maxDebounceDelay, TimeSpan retryDelay)
        {
            if (debounceDelay <= TimeSpan.Zero || maxDebounceDelay <= TimeSpan.Zero || retryDelay <= TimeSpan.Zero)
            {
                throw new ArgumentException("every delay of the reload cycle must be positive");
            }
            if (debounceDelay > maxDebounceDelay)
            {
                throw new ArgumentException("the settle window must not be longer than the bound on postponing a reload");
            }
            DebounceDelay = debounceDelay;
            MaxDebounceDelay = maxDebounceDelay;
            RetryDelay = retryDelay;
        }
    }

    /// <summary>
    /// Owns the reload cycle for a set of data files. It serializes reloads, debounces change
    /// signals, retains the last good result on failure by not calling Apply, retries after
    /// failures up to an optional bound, and skips no-op applications.
    /// </summary>
    internal sealed class FileDataReloader : IDisposable
    {
        private readonly FileDataReloaderConfig _config;
        private readonly FileDataReloaderTiming _timing;
        private readonly FileDataParser _parser;
        private readonly FileDataTypes.IFileReader _fileReader;
        private readonly Logger _log;

        // Serializes the load work between ReloadNow and the timer callbacks.
        private readonly object _reloadLock = new object();
        private byte[] _lastGoodHash; // only touched inside _reloadLock
        private string _lastErrorMessage; // only touched inside _reloadLock
        private int _failedAttempts; // only touched inside _reloadLock

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

        // Guards the state of triggered reloads. One thread at a time performs them; a request that
        // arrives while one runs is remembered and served by one follow-up reload.
        private readonly object _pendingLock = new object();
        private bool _reloading;
        private bool _reloadPending;

        private volatile bool _disposed;

        /// <summary>
        /// Constructs a reloader. Nothing is loaded until <see cref="ReloadNow"/> or
        /// <see cref="Trigger"/> is called.
        /// </summary>
        /// <param name="config">what to load and what to do with it</param>
        /// <param name="timing">the delays of the reload cycle; null for the production delays. Tests
        /// pass shorter delays so that they do not wait for the production ones.</param>
        internal FileDataReloader(FileDataReloaderConfig config, FileDataReloaderTiming timing = null)
        {
            _config = config;
            _timing = timing ?? FileDataReloaderTiming.Production;
            _parser = new FileDataParser(config.ParsersByExtension);
            _fileReader = config.FileReader ?? FileDataReader.Instance;
            _log = config.Logger ?? Logs.None.Logger("");
        }

        /// <summary>
        /// Synchronously loads the files and applies the result, or reports the failure. Use it
        /// for the initial load. A failure here arms the same automatic retry as a failed
        /// triggered reload.
        /// </summary>
        internal void ReloadNow()
        {
            if (_disposed)
            {
                return;
            }
            // A success does not disarm a pending retry. The reload lock is released when the load
            // ends, so a disarm here could cancel a retry that a later failed load on another thread
            // armed. A retry that is left over from an earlier failure does no work: Reload skips a
            // retry when nothing has failed since the last success.
            if (Reload(isRetry: false) == LoadOutcome.Failed)
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
                var delay = _timing.DebounceDelay;
                var elapsed = TimeSpan.FromTicks((now - _burstStartTimestamp) * TimeSpan.TicksPerSecond /
                    Stopwatch.Frequency);
                var remaining = _timing.MaxDebounceDelay - elapsed;
                if (remaining < delay)
                {
                    delay = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
                }
                _debounceTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Stops the reloader. It does not wait for a reload that is already in progress. A reload
        /// wedged in a blocking file read must not be able to wedge shutdown. Such a reload can
        /// still deliver its result through Apply shortly after Dispose returns. A reload that has
        /// not yet reached Apply when Dispose is called does not invoke it.
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
            if (Reload(isRetry) == LoadOutcome.Failed)
            {
                ArmRetry(keepExistingDeadline: false);
            }
        }

        private void ArmRetry(bool keepExistingDeadline)
        {
            if (!_config.RetryOnFailure)
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
                _retryTimer.Change(_timing.RetryDelay, Timeout.InfiniteTimeSpan);
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

        // What one load did, which decides what happens to the retry: a failure arms it, and a
        // success or giving up arms nothing.
        private enum LoadOutcome
        {
            Succeeded,
            Failed,
            GaveUp
        }

        // Performs one full load of all configured files. A skipped no-op application counts as
        // success. The whole set is re-read on every reload: entries are combined across files in
        // order, so a change to one file can alter which file wins for a key.
        private LoadOutcome Reload(bool isRetry)
        {
            lock (_reloadLock)
            {
                // A trigger already queued when Dispose was called can still reach here.
                if (_disposed)
                {
                    return LoadOutcome.Succeeded;
                }
                if (!isRetry)
                {
                    // A load that is not a retry starts a new run of attempts with a fresh budget.
                    _failedAttempts = 0;
                }
                else if (_failedAttempts == 0)
                {
                    // The retry is stale: a load succeeded after the failure that armed it. Its
                    // timer fired while that load was running, or the load was a ReloadNow, which
                    // does not disarm the retry. Nothing has failed since, so there is nothing to
                    // retry, and applying the same data again would be a spurious change for a
                    // consumer that stamps each application with a version.
                    return LoadOutcome.Succeeded;
                }

                var documents = new List<FileDataDocument>(_config.Paths.Count);
                var files = new List<FileDataFileSummary>(_config.Paths.Count);
                using (var hasher = _config.SkipUnchanged ? SHA256.Create() : null)
                {
                    return Reload(documents, files, hasher, isRetry);
                }
            }
        }

        // The body of Reload, inside _reloadLock. The hasher is null when unchanged content is not
        // skipped.
        private LoadOutcome Reload(List<FileDataDocument> documents, List<FileDataFileSummary> files, SHA256 hasher,
            bool isRetry)
        {
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
                    AppendToHash(hasher, path, null);
                    continue;
                }
                catch (DirectoryNotFoundException) when (_config.SkipMissingPaths &&
                    !_config.FailOnMissingDirectory)
                {
                    _log.Debug("File {0} does not exist; it contributes no data", path);
                    files.Add(new FileDataFileSummary(path, false, 0, 0));
                    AppendToHash(hasher, path, null);
                    continue;
                }
                catch (Exception e)
                {
                    return Fail(new FileDataReadException(path, "unable to read file: " + e.Message, e), isRetry);
                }
                // One read feeds both the hash and the parse, so the skip-unchanged hash can never
                // disagree with the content that was actually applied.
                AppendToHash(hasher, path, content);
                FileDataDocument document;
                try
                {
                    document = _parser.Parse(path, content);
                }
                catch (Exception e)
                {
                    return Fail(new FileDataReadException(path, "error parsing file: " + e.Message, e), isRetry);
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
                return Fail(e, isRetry);
            }
            catch (Exception e)
            {
                // The flag value expander is supplied by the consumer. Its failure is a failure
                // to process the files, like a merge failure.
                return Fail(new FileDataException("error expanding flag values: " + e.Message, e), isRetry);
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
                return LoadOutcome.Succeeded;
            }

            // A success right after a failure must apply even when the content is unchanged since
            // the last success. The failure was logged, and only an Apply, with whatever the
            // consumer logs for it, shows that things are good again.
            var recovering = _lastErrorMessage != null;
            byte[] hash = null;
            if (hasher != null)
            {
                hash = FinishHash(hasher);
                if (!recovering && _lastGoodHash != null && hash.SequenceEqual(_lastGoodHash))
                {
                    return LoadOutcome.Succeeded;
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
                return Fail(new FileDataException("error applying file data: " + e.Message, e), isRetry);
            }
            _lastErrorMessage = null;
            _lastGoodHash = hash;
            _failedAttempts = 0;
            return LoadOutcome.Succeeded;
        }

        // Called inside _reloadLock.
        private LoadOutcome Fail(Exception e, bool isRetry)
        {
            // Dispose may have happened while the files were being read. Deliver nothing in that
            // case, and report success so no retry is armed.
            if (_disposed)
            {
                return LoadOutcome.Succeeded;
            }
            // With automatic retries, a persistent failure would repeat the same log entry on every
            // attempt. A retry that fails the same way as the attempt before it is demoted to debug
            // level. A load that a change signal or ReloadNow started is a new event and is always
            // logged at error level.
            if (isRetry && e.Message == _lastErrorMessage)
            {
                _log.Debug("Unable to load flags: {0}", e.Message);
            }
            else
            {
                _lastErrorMessage = e.Message;
                _log.Error("Unable to load flags: {0}", e.Message);
            }
            _failedAttempts++;
            // The bound only matters where a retry would otherwise be armed. The next load that is
            // not a retry starts over with a fresh budget.
            if (_config.MaxLoadAttempts > 0 && _config.RetryOnFailure &&
                _failedAttempts >= _config.MaxLoadAttempts)
            {
                // The reason goes on this line too: the attempts before it may have repeated an
                // earlier failure at debug level only.
                _log.Error("Unable to load flags after {0} attempts; not retrying until the next detected change: {1}",
                    _failedAttempts, e.Message);
                return LoadOutcome.GaveUp;
            }
            return LoadOutcome.Failed;
        }

        // Adds one file to the skip-unchanged hash: its path, whether it is present, and the length
        // and bytes of its content. The framing makes the same bytes in a different file, or a file
        // that appears or disappears, a change. A null content means the file is absent.
        private static void AppendToHash(SHA256 hasher, string path, string content)
        {
            if (hasher == null)
            {
                return;
            }
            AppendBytesToHash(hasher, Encoding.UTF8.GetBytes(path));
            hasher.TransformBlock(new[] { content is null ? (byte)0 : (byte)1 }, 0, 1, null, 0);
            if (content != null)
            {
                AppendBytesToHash(hasher, Encoding.UTF8.GetBytes(content));
            }
        }

        private static void AppendBytesToHash(SHA256 hasher, byte[] bytes)
        {
            var length = BitConverter.GetBytes((long)bytes.Length);
            hasher.TransformBlock(length, 0, length.Length, null, 0);
            if (bytes.Length > 0)
            {
                hasher.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
        }

        private static byte[] FinishHash(SHA256 hasher)
        {
            hasher.TransformFinalBlock(new byte[0], 0, 0);
            return hasher.Hash;
        }
    }
}
