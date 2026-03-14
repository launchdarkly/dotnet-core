using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// A composite source is a source that can dynamically switch between sources with the
    /// help of a list of <see cref="SourceFactory"/> delegates and <see cref="ActionApplierFactory"/> delegates.
    /// The SourceFactory delegates are used to create the data sources, and the ActionApplierFactory creates the action appliers that are used
    ///  to apply actions to the composite source as updates are received from the data sources.
    /// </summary>
    internal sealed class CompositeSource : IDataSource, ICompositeSourceActionable
    {
        // All mutable state and the internal action queue are protected by this lock.
        // We also use a small, non-recursive action queue so that any re-entrant calls
        // from action applier logic is serialized and processed iteratively instead
        // of recursively, avoiding the risk of stack overflows.
        private readonly string _compositeDescription;
        private readonly Logger _log;
        private readonly object _lock = new object();
        private readonly object _actionQueueLock = new object();
        private readonly Queue<Action> _pendingActions = new Queue<Action>();
        private bool _actionQueueShutdown = false;
        private bool _isProcessingActions;
        private bool _disposed;

        private readonly IDataSourceUpdatesV2 _originalUpdateSink;
        private readonly IDataSourceUpdatesV2 _sanitizedUpdateSink;
        private readonly SourcesList<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)> _sourcesList;
        private readonly DisableableDataSourceUpdatesTracker _disableableTracker;

        // Tracks the entry from the sources list that was used to create the current
        // data source instance. This allows operations such as blacklist to remove
        // the correct factory/action-applier-factory tuple from the list.
        private (SourceFactory Factory, ActionApplierFactory ActionApplierFactory) _currentEntry;
        private IDataSource _currentDataSource;

        /// <summary>
        /// Creates a new <see cref="CompositeSource"/>.
        /// </summary>
        /// <param name="compositeDescription">description of the composite source for logging purposes</param>
        /// <param name="updatesSink">the sink that receives updates from the active source</param>
        /// <param name="factoryTuples">the ordered list of source factories and their associated action applier factories</param>
        /// <param name="logger">the logger instance to use</param>
        /// <param name="circular">whether to loop off the end of the list back to the start when fallback occurs</param>
        public CompositeSource(
            string compositeDescription,
            IDataSourceUpdatesV2 updatesSink,
            IList<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)> factoryTuples,
            Logger logger,
            bool circular = true)
        {
            if (updatesSink is null)
            {
                throw new ArgumentNullException(nameof(updatesSink));
            }
            if (factoryTuples is null)
            {
                throw new ArgumentNullException(nameof(factoryTuples));
            }

            _compositeDescription = compositeDescription;
            _log = logger;

            _originalUpdateSink = updatesSink;
            _sanitizedUpdateSink = new DataSourceUpdatesSanitizer(updatesSink);

            // this tracker is used to disconnect the current source from the updates sink when it is no longer needed.
            _disableableTracker = new DisableableDataSourceUpdatesTracker();

            _sourcesList = new SourcesList<(SourceFactory SourceFactory, ActionApplierFactory ActionApplierFactory)>(
                circular: circular,
                initialList: factoryTuples
            );
        }

        /// <summary>
        /// Returns a string representation of this data source for informational purposes.
        /// </summary>
        public override string ToString() => _compositeDescription;

        /// <summary>
        /// When <see cref="Start"/> is called, the current data source is started. This should only be called once.
        /// </summary>
        /// <returns>
        /// a task that completes when the underlying current source has finished starting
        /// </returns>
        public Task<bool> Start()
        {
            return StartCurrent();
        }

        /// <summary>
        /// Returns whether the current underlying data source has finished initializing.
        /// </summary>
        public bool Initialized => _currentDataSource?.Initialized ?? false;

        /// <summary>
        /// Disposes of the composite data source.
        /// </summary>
        public void Dispose()
        {
            InternalDispose();
        }

        private void InternalDispose(DataSourceStatus.ErrorInfo? error = null)
        {
            lock (_actionQueueLock)
            {
                _pendingActions.Clear();
                _actionQueueShutdown = true;
            }
            // When disposing the whole composite, we bypass the action queue and tear
            // down the current data source immediately while still honoring the same
            // state transitions under the shared lock. Any queued actions become no-ops
            // because there is no current data source
            // been disconnected.
            lock (_lock)
            {
                // cut off all the update proxies that have been handed out first
                _disableableTracker.DisablePreviouslyTracked();

                // dispose of the current data source
                _currentDataSource?.Dispose();
                _currentDataSource = null;
                _currentEntry = default;
                _disposed = true;
            }

            // report state Off directly to the original sink, bypassing the sanitizer
            // which would map Off to Interrupted (that mapping is only for underlying sources)
            _originalUpdateSink.UpdateStatus(DataSourceState.Off, error);
        }

        /// <summary>
        /// Enqueue a state-changing operation to be executed under the shared lock.
        /// If no other operation is currently running, this will asynchronously process
        /// the queue on a background thread. Any re-entrant calls from within the operations
        /// will only enqueue more work; they will not trigger another processing loop, so
        /// the call stack does not grow with the queue length. Processing actions on a
        /// background thread prevents blocking the calling thread and allows operations like
        /// disposal to proceed even when actions are continuously being enqueued.
        /// </summary>
        private void EnqueueAction(Action action)
        {
            bool shouldProcess = false;
            lock (_actionQueueLock)
            {
                if (_actionQueueShutdown)
                {
                    return;
                }
                _pendingActions.Enqueue(action);
                if (!_isProcessingActions)
                {
                    _isProcessingActions = true;
                    shouldProcess = true;
                }
            }

            if (shouldProcess)
            {
                // Process actions on a background thread to prevent blocking the caller
                // and allow Start() to return even when actions are continuously enqueued
                _ = Task.Run(() => ProcessQueuedActions());
            }
        }

        /// <summary>
        /// Processes the queued actions on a background thread.
        /// </summary>
        private void ProcessQueuedActions()
        {
            while (true)
            {
                Action action;
                lock (_actionQueueLock)
                {
                    // Check if disposed to allow disposal to interrupt action processing
                    if (_actionQueueShutdown || _pendingActions.Count == 0)
                    {
                        _isProcessingActions = false;
                        return;
                    }

                    action = _pendingActions.Dequeue();
                }

                // Execute outside of the lock so that operations can do more work,
                // including calling back into the composite and queuing additional
                // actions without blocking the queue.
                // If an action throws an exception, catch it and continue processing
                // the next action to prevent one failure from stopping all processing.
                try
                {
                    action();
                }
                catch
                {
                    // Continue processing remaining actions even if one fails
                    // TODO: need to add logging, will add in next PR
                }
            }
        }

        // This method must only be called while holding _lock.
        private void TryFindNextUnderLock()
        {
            if (_currentDataSource != null)
            {
                return;
            }

            var entry = _sourcesList.Next();

            if (entry.Factory == null)
            {
                // Failed to find a next source, report error and shut down the composite source
                var errorInfo = new DataSourceStatus.ErrorInfo
                {
                    Kind = DataSourceStatus.ErrorKind.Unknown,
                    Message = "Composite source " + _compositeDescription + " has exhausted its constituent sources.",
                    Time = DateTime.Now
                };
                InternalDispose(errorInfo);
                return;
            }
            
            IReadOnlyList<IDataSourceObserver> observers;
            // Conditionally add the action applier if we have one
            if (entry.ActionApplierFactory != null)
            {
                var observersList = new List<IDataSourceObserver>();
                var actionApplier = entry.ActionApplierFactory(this);
                observersList.Add(actionApplier);
                observers = observersList;
            }
            else
            {
                observers = Array.Empty<IDataSourceObserver>();
            }

            // here we wrap the sink in observability so that we can trigger actions, the sanitized sink is
            // invoked before the observers to ensure actions don't trigger before data can propagate
            var observableUpdates = new ObservableDataSourceUpdates(_sanitizedUpdateSink, observers);
            var disableableUpdates = _disableableTracker.WrapAndTrack(observableUpdates);
            
            _currentEntry = entry;
            _currentDataSource = entry.Factory(disableableUpdates);
        }

        #region ICompositeSourceActionable

        public Task<bool> StartCurrent()
        {
            if (_disposed)
            {
                return Task.FromResult(false);
            }

            var tcs = new TaskCompletionSource<bool>();
            
            EnqueueAction(() =>
            {
                try
                {
                    IDataSource dataSourceToStart;
                    bool disposed;
                    lock (_lock)
                    {
                        TryFindNextUnderLock();
                        dataSourceToStart = _currentDataSource;
                        disposed = _disposed;
                    }

                    if (disposed)
                    {
                        // Disposed while getting the data source.
                        tcs.SetResult(false);
                        return;
                    }

                    if (dataSourceToStart is null)
                    {
                        // No sources available.
                        tcs.SetResult(false);
                        return;
                    }

                    // Start the source asynchronously and complete the task when it finishes.
                    // We do this outside the lock to avoid blocking the queue.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            _log.Debug("{0} started {1}.", _compositeDescription, dataSourceToStart.ToString());
                            var result = await dataSourceToStart.Start().ConfigureAwait(false);
                            tcs.TrySetResult(result);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    // If an exception occurs while finding or setting up the data source,
                    // ensure the task is completed so callers don't wait indefinitely.
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        public void DisposeCurrent()
        {
            if (_disposed)
            {
                return;
            }

            EnqueueAction(() =>
            {
                lock (_lock)
                {
                    String currentDescription = _currentDataSource?.ToString();
                    _log.Debug("{0} is going to dispose of {1}.", _compositeDescription, currentDescription);

                    // cut off all the update proxies that have been handed out first, this is
                    // necessary to avoid a cascade of actions leading to callbacks leading to actions, etc.
                    _disableableTracker.DisablePreviouslyTracked();

                    // dispose of the current data source
                    _currentDataSource?.Dispose();
                    _currentDataSource = null;

                    // spoof interrupted (if the underlying source reported interrupted, sanitizer will not report it again)
                    _sanitizedUpdateSink.UpdateStatus(DataSourceState.Interrupted, null);
                }
            });
        }

        public void GoToNext()
        {
            if (_disposed)
            {
                return;
            }

            EnqueueAction(() =>
            {
                lock (_lock)
                {
                    // Get description of current source before disposing it
                    String previousDescription = _currentDataSource?.ToString();

                    // cut off all the update proxies that have been handed out first, this is
                    // necessary to avoid a cascade of actions leading to callbacks leading to actions, etc.
                    _disableableTracker.DisablePreviouslyTracked();

                    _currentDataSource?.Dispose();
                    _currentDataSource = null;

                    // spoof interrupted (if the underlying source reported interrupted, sanitizer will not report it again)
                    _sanitizedUpdateSink.UpdateStatus(DataSourceState.Interrupted, null);

                    TryFindNextUnderLock();

                    String currentDescription = _currentDataSource?.ToString();
                    logTransition(previousDescription, currentDescription);  
                }
            });
        }

        public void GoToFirst()
        {
            if (_disposed)
            {
                return;
            }

            EnqueueAction(() =>
            {
                lock (_lock)
                {
                    String previousDescription = _currentDataSource?.ToString();

                    // moving always disconnects the current source
                    _disableableTracker.DisablePreviouslyTracked();

                    _currentDataSource?.Dispose();
                    _currentDataSource = null;

                    // spoof interrupted (if the underlying source reported interrupted, sanitizer will not report it again)
                    _sanitizedUpdateSink.UpdateStatus(DataSourceState.Interrupted, null);

                    _sourcesList.Reset();
                    TryFindNextUnderLock();

                    String currentDescription = _currentDataSource?.ToString();

                    logTransition(previousDescription, currentDescription);
                }
            });
        }

        public bool IsAtFirst()
        {
            lock (_lock)
            {
                if (_currentEntry == default)
                {
                    return false; // No current entry means we're not at first
                }
                return _sourcesList.IndexOf(_currentEntry) == 0;
            }
        }

        public void BlockCurrent()
        {
            if (_disposed)
            {
                return;
            }

            EnqueueAction(() =>
            {
                lock (_lock)
                {
                    // If we've never had a current entry, there's nothing to blacklist.
                    if (_currentEntry == default)
                    {
                        return;
                    }

                    String currentDescription = _currentDataSource?.ToString();

                    // remove the factory tuple for our current entry
                    // note: blacklisting does not tear down the current data source, it just prevents it from being used again
                    _sourcesList.Remove(_currentEntry);
                    _currentEntry = default;

                    _log.Debug("{0} has blocked factory used to create {1} from being used again.", _compositeDescription, currentDescription);
                }
            });
        }

        private void logTransition(String previousDescription, String currentDescription) {
            if (previousDescription != null && currentDescription != null) {
                _log.Debug("{0} transitioned from {1} to {2}.", _compositeDescription, previousDescription, currentDescription);
            } else if (previousDescription != null) {
                _log.Debug("{0} transitioned away from {1}.", _compositeDescription, previousDescription);
            } else if (currentDescription != null) {
                _log.Debug("{0} at {1}.", _compositeDescription, currentDescription);
            }
        }

        #endregion
    }
}
