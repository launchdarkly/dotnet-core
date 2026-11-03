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
        /// When true, a configured file that does not exist contributes no data. The reload succeeds
        /// with the data of the files that exist. When false, a missing file
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
        /// change notification arrives, and a file whose content is in effect and that is found empty
        /// is held back until it has stayed empty, without being written, for the same delay. If
        /// false, a failed reload waits for the next trigger, and an empty file is parsed as it is.
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
        // Whether each configured path contributed content to the data in effect, that is, to the last
        // load that was applied; only touched inside _reloadLock. A missing or empty file contributed
        // none.
        private readonly Dictionary<string, bool> _appliedHadContent = new Dictionary<string, bool>();
        // The files whose content is in effect and that loads have found with no entries since the
        // data in effect was applied; only touched inside _reloadLock. Applying data clears it. See
        // ShouldHoldBack.
        private readonly Dictionary<string, EmptyFile> _emptyFiles = new Dictionary<string, EmptyFile>();
        // True from a hold (see HoldBack) until the next load starts; only touched inside
        // _reloadLock. The retry it arms is not stale although nothing failed.
        private bool _readAgainPending;

        private sealed class EmptyFile
        {
            // Since when the file has had no entries without being written, and its state then. Null
            // when the last load that read the file found content in it.
            internal TimeSpan? Since;
            internal FileState State;
            // When a load first held the file back since the data in effect was applied, or since the
            // file last stayed empty for the whole delay; null when no load has.
            internal TimeSpan? FirstHeld;
        }

        // A file stops being held back once loads have held it back for this many retry delays.
        private const int MaxHoldRetryDelays = 5;

        // Guards the timers. The timers are created on first use so that a reloader that is never
        // used holds no scheduled work.
        private readonly object _timerLock = new object();
        private Timer _debounceTimer;
        // The time of the first trigger of the pending stream, or null when no reload is pending.
        // Guarded by _timerLock.
        private TimeSpan? _burstStart;
        private Timer _retryTimer;
        private bool _retryArmed;

        // Guards the state of triggered reloads. One thread at a time performs them; a request that
        // arrives while one runs is remembered and served by one follow-up reload.
        private readonly object _pendingLock = new object();
        private bool _reloading;
        private bool _reloadPending;

        private volatile bool _disposed;

        // A monotonic clock. Stopwatch.Elapsed converts its ticks without overflow.
        private static readonly Stopwatch MonotonicStopwatch = Stopwatch.StartNew();
        private readonly Func<TimeSpan> _now;

        /// <summary>
        /// Constructs a reloader. Nothing is loaded until <see cref="ReloadNow"/> or
        /// <see cref="Trigger"/> is called.
        /// </summary>
        /// <param name="config">what to load and what to do with it</param>
        /// <param name="timing">the delays of the reload cycle; null for the production delays. Tests
        /// pass shorter delays so that they do not wait for the production ones.</param>
        /// <param name="clock">a monotonic clock; null for the system's. Tests pass a clock that they
        /// move on themselves.</param>
        internal FileDataReloader(FileDataReloaderConfig config, FileDataReloaderTiming timing = null,
            Func<TimeSpan> clock = null)
        {
            _now = clock ?? (() => MonotonicStopwatch.Elapsed);
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
            if (Reload(isRetry: false, out var retryDelay) == LoadOutcome.Failed)
            {
                // An already armed retry keeps its earlier deadline.
                ArmRetry(keepExistingDeadline: true, retryDelay);
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
                var now = _now();
                if (_burstStart == null)
                {
                    _burstStart = now;
                }
                var delay = _timing.DebounceDelay;
                var remaining = _timing.MaxDebounceDelay - (now - _burstStart.Value);
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
                _burstStart = null;
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
            if (!isRetry)
            {
                _log.Info("Reloading file data after detecting a change");
            }
            // A pending retry is superseded by this reload. It either succeeds, or it fails and
            // arms a fresh retry.
            DisarmRetry();
            if (Reload(isRetry, out var retryDelay) == LoadOutcome.Failed)
            {
                ArmRetry(keepExistingDeadline: false, retryDelay);
            }
        }

        // The delay of the last retry that a load asked for, armed or not. Only tests read it.
        internal TimeSpan? LastRequestedRetryDelay
        {
            get
            {
                lock (_timerLock)
                {
                    return _lastRequestedRetryDelay;
                }
            }
        }
        private TimeSpan? _lastRequestedRetryDelay; // guarded by _timerLock

        private void ArmRetry(bool keepExistingDeadline, TimeSpan delay)
        {
            if (!_config.RetryOnFailure)
            {
                return;
            }
            lock (_timerLock)
            {
                _lastRequestedRetryDelay = delay;
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
                _retryTimer.Change(delay, Timeout.InfiniteTimeSpan);
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
        // A failed load leaves in retryDelay how long to wait before retrying it.
        private LoadOutcome Reload(bool isRetry, out TimeSpan retryDelay)
        {
            retryDelay = _timing.RetryDelay;
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
                else if (_failedAttempts == 0 && !_readAgainPending)
                {
                    // The retry is stale: a load succeeded after the failure that armed it. Its
                    // timer fired while that load was running, or the load was a ReloadNow, which
                    // does not disarm the retry. Nothing has failed since, so there is nothing to
                    // retry, and applying the same data again would be a spurious change for a
                    // consumer that stamps each application with a version.
                    return LoadOutcome.Succeeded;
                }

                if (isRetry)
                {
                    _log.Debug(_readAgainPending ? "Reading file data again after a file was found empty"
                        : "Retrying file data load after earlier failure");
                }
                _readAgainPending = false;

                var documents = new List<FileDataDocument>(_config.Paths.Count);
                var files = new List<FileDataFileSummary>(_config.Paths.Count);
                var hadContent = new Dictionary<string, bool>(_config.Paths.Count);
                using (var hasher = _config.SkipUnchanged ? SHA256.Create() : null)
                {
                    return Reload(documents, files, hadContent, hasher, isRetry, ref retryDelay);
                }
            }
        }

        // The body of Reload, inside _reloadLock. The hasher is null when unchanged content is not
        // skipped.
        private LoadOutcome Reload(List<FileDataDocument> documents, List<FileDataFileSummary> files,
            Dictionary<string, bool> hadContent, SHA256 hasher, bool isRetry, ref TimeSpan retryDelay)
        {
            // The files that ShouldHoldBack holds back. The other files are still read, so that every
            // file that is found empty starts its delay in this load.
            List<string> heldBack = null;
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
                    hadContent[path] = false;
                    files.Add(new FileDataFileSummary(path, false, 0, 0));
                    AppendToHash(hasher, path, null);
                    continue;
                }
                catch (DirectoryNotFoundException) when (_config.SkipMissingPaths &&
                    !_config.FailOnMissingDirectory)
                {
                    _log.Debug("File {0} does not exist; it contributes no data", path);
                    hadContent[path] = false;
                    files.Add(new FileDataFileSummary(path, false, 0, 0));
                    AppendToHash(hasher, path, null);
                    continue;
                }
                catch (Exception e)
                {
                    return Fail(new FileDataReadException(path, "unable to read file: " + e.Message, e), isRetry);
                }
                // A file with no content, or with content that a registered parser reads as nothing,
                // such as YAML with only a comment, has no entries. A read can see that much of a
                // file that is being written. Content with no document is invalid JSON, so it is only
                // parsed when the file is not held back.
                FileDataDocument document = null;
                var hasContent = !string.IsNullOrWhiteSpace(content);
                if (hasContent)
                {
                    try
                    {
                        document = _parser.Parse(path, content);
                    }
                    catch (Exception e)
                    {
                        return Fail(new FileDataReadException(path, "error parsing file: " + e.Message, e), isRetry);
                    }
                    hasContent = !ReferenceEquals(document, FileDataDocument.Empty);
                }
                if (hasContent)
                {
                    if (_emptyFiles.TryGetValue(path, out var empty))
                    {
                        empty.Since = null;
                    }
                }
                // The clock is read after this file's own read, so a slow read of another file does not
                // shorten its delay.
                else if (ShouldHoldBack(path, _now()))
                {
                    (heldBack = heldBack ?? new List<string>()).Add(path);
                    continue;
                }
                hadContent[path] = hasContent;
                // One read feeds both the hash and the parse, so the skip-unchanged hash can never
                // disagree with the content that was actually applied.
                AppendToHash(hasher, path, content);
                if (document == null)
                {
                    try
                    {
                        document = _parser.Parse(path, content);
                    }
                    catch (Exception e)
                    {
                        return Fail(new FileDataReadException(path, "error parsing file: " + e.Message, e), isRetry);
                    }
                }
                documents.Add(document);
                files.Add(new FileDataFileSummary(path, true, 0, 0));
            }
            if (heldBack != null)
            {
                return HoldBack(heldBack, _now(), out retryDelay);
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
                    // The content is the same as that of the data in effect, so the files are in effect
                    // as they are now, as after an application. A file that was held back and has been
                    // rewritten with the same content gets a fresh bound for its next rewrite.
                    RememberAppliedContent(hadContent);
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
            RememberAppliedContent(hadContent);
            return LoadOutcome.Succeeded;
        }

        // Called inside _reloadLock when the data of a load is in effect.
        private void RememberAppliedContent(Dictionary<string, bool> hadContent)
        {
            foreach (var entry in hadContent)
            {
                _appliedHadContent[entry.Key] = entry.Value;
            }
            _emptyFiles.Clear();
        }

        // Called inside _reloadLock when a load finds a file with no entries. A file whose content is
        // in effect and that has none now may be in the middle of an in-place rewrite: a writer that
        // truncates the file and then writes the new content. Such a file is held back until it has
        // stayed so, without being written, for the retry delay. A change of its state, its
        // modification time and size, means that it was written since a load last found it so, for
        // example rewritten and truncated again between two loads, so the delay starts over. The state
        // is read through a symbolic link where the framework can resolve links; elsewhere a link is
        // observed by its content, which is the same for every empty state.
        //
        // A file that keeps being written while every load finds it empty, or files that are rewritten
        // in turn so that every load finds one of them empty, would be held back without end. So a
        // file stops being held back once loads have held it back for MaxHoldRetryDelays retry delays.
        // That bound is the file's own: another file's hold or a failed load does not shorten or reset
        // it. It starts over when data is applied, or when the file has stayed empty for the whole
        // delay, so that a later rewrite is held back again.
        //
        // A file whose content is not in effect, such as a placeholder that has always been empty, is
        // parsed right away, and so is any file when retries are off, since nothing would read it
        // again.
        private bool ShouldHoldBack(string path, TimeSpan now)
        {
            if (!_config.RetryOnFailure || !(_appliedHadContent.TryGetValue(path, out var applied) && applied))
            {
                return false;
            }
            var state = FileState.Observe(path);
            if (!_emptyFiles.TryGetValue(path, out var empty))
            {
                empty = new EmptyFile();
                _emptyFiles[path] = empty;
            }
            if (empty.Since == null || !empty.State.Equals(state))
            {
                empty.Since = now;
                empty.State = state;
            }
            if (now - empty.Since.Value >= _timing.RetryDelay)
            {
                empty.FirstHeld = null;
                return false;
            }
            if (empty.FirstHeld != null && now - empty.FirstHeld.Value >= MaxHoldDuration)
            {
                return false;
            }
            empty.FirstHeld = empty.FirstHeld ?? now;
            return true;
        }

        private TimeSpan MaxHoldDuration => TimeSpan.FromTicks(_timing.RetryDelay.Ticks * MaxHoldRetryDelays);

        // Called inside _reloadLock, for the files that ShouldHoldBack holds back. Whatever the files'
        // format, the load keeps the current data and arms the retry without logging an error or using
        // up an attempt. The retry comes when the first of the files has stayed empty for the retry
        // delay, or has been held back for as long as it may be, plus a tenth of the retry delay, so
        // that a timer that fires a little early still finds the time over; the change signal for a
        // write starts a load sooner. Once a file has stayed empty for the retry
        // delay, a load parses it: a file that is still empty is invalid JSON, or a document with no
        // entries in a format such as YAML.
        private LoadOutcome HoldBack(List<string> paths, TimeSpan now, out TimeSpan retryDelay)
        {
            retryDelay = _timing.RetryDelay;
            if (_disposed)
            {
                return LoadOutcome.Succeeded;
            }
            var until = TimeSpan.MaxValue;
            foreach (var path in paths)
            {
                _log.Debug("File {0} is empty, possibly while it is being rewritten; reading it again before applying it", path);
                var empty = _emptyFiles[path];
                var delayOver = empty.Since.Value + _timing.RetryDelay;
                var boundOver = empty.FirstHeld.Value + MaxHoldDuration;
                var over = delayOver < boundOver ? delayOver : boundOver;
                if (over < until)
                {
                    until = over;
                }
            }
            var margin = TimeSpan.FromTicks(_timing.RetryDelay.Ticks / 10);
            retryDelay = (until > now ? until - now : TimeSpan.Zero) + margin;
            _readAgainPending = true;
            return LoadOutcome.Failed;
        }

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
