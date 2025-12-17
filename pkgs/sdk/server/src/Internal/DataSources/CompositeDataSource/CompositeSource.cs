using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

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
        private readonly object _lock = new object();
        private readonly Queue<Action> _pendingActions = new Queue<Action>();
        private bool _isProcessingActions;
        private bool _disposed;
        
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);
        private readonly TaskCompletionSource<bool> _initializedTask = new TaskCompletionSource<bool>();

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
        /// <param name="updatesSink">the sink that receives updates from the active source</param>
        /// <param name="factoryTuples">the ordered list of source factories and their associated action applier factories</param>
        /// <param name="circular">whether to loop off the end of the list back to the start when fallback occurs</param>
        public CompositeSource(
            IDataSourceUpdatesV2 updatesSink,
            IList<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)> factoryTuples,
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
        /// When <see cref="Start"/> is called, the current data source is started. This should only be called once.
        /// </summary>
        /// <returns>
        /// a task that completes when the underlying current source has finished starting
        /// </returns>
        public Task<bool> Start()
        {
            _ = StartCurrent();
            return _initializedTask.Task;
        }

        /// <summary>
        /// Returns whether the current underlying data source has finished initializing.
        /// </summary>
        public bool Initialized => _initialized.Get();

        /// <summary>
        /// Disposes of the composite data source.
        /// </summary>
        public void Dispose()
        {
            InternalDispose();
        }

        private void InternalDispose(DataSourceStatus.ErrorInfo? error = null)
        {
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

                // clear any queued actions and reset processing state
                _pendingActions.Clear();
                _isProcessingActions = false;
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
            lock (_lock)
            {
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
                lock (_lock)
                {
                    // Check if disposed to allow disposal to interrupt action processing
                    if (_disposed || _pendingActions.Count == 0)
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
                    Message = "CompositeDataSource has exhausted all available sources.",
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

        /// <summary>
        /// Starts the current data source.
        /// </summary>
        public Task<bool> StartCurrent()
        {
            if (_disposed)
            {
                return Task.FromResult(false);
            }

            var tcs = new TaskCompletionSource<bool>();
            
            EnqueueAction(() =>
            {
                IDataSource dataSourceToStart;
                lock (_lock)
                {
                    TryFindNextUnderLock();
                    dataSourceToStart = _currentDataSource;
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
                        var result = await dataSourceToStart.Start().ConfigureAwait(false);
                        tcs.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
            });

            return tcs.Task;
        }

        /// <summary>
        /// Disposes of the current data source. You must call GoToNext or GoToFirst after this to change to a new factory.
        /// </summary>
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

        /// <summary>
        /// Switches to the next source in the list. You must still call StartCurrent after this to actually start the new source.
        /// </summary>
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
                    // cut off all the update proxies that have been handed out first, this is
                    // necessary to avoid a cascade of actions leading to callbacks leading to actions, etc.
                    _disableableTracker.DisablePreviouslyTracked();

                    _currentDataSource?.Dispose();
                    _currentDataSource = null;

                    // spoof interrupted (if the underlying source reported interrupted, sanitizer will not report it again)
                    _sanitizedUpdateSink.UpdateStatus(DataSourceState.Interrupted, null);

                    TryFindNextUnderLock();

                    // if there is no next source, there's nothing more to do
                }
            });
        }

        /// <summary>
        /// Switches to the first source in the list. You must still call StartCurrent after this to actually start the new source.
        /// </summary>
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
                    // moving always disconnects the current source
                    _disableableTracker.DisablePreviouslyTracked();

                    _currentDataSource?.Dispose();
                    _currentDataSource = null;

                    // spoof interrupted (if the underlying source reported interrupted, sanitizer will not report it again)
                    _sanitizedUpdateSink.UpdateStatus(DataSourceState.Interrupted, null);

                    _sourcesList.Reset();
                    TryFindNextUnderLock();

                    // if there are no sources, there's nothing more to do
                }
            });
        }

        /// <summary>
        /// Blacklists the current source. This prevents the current source from being used again. 
        /// Note that blacklisting does not tear down the current data source, it just prevents it from being used again.
        /// </summary>
        public void BlacklistCurrent()
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

                    // remove the factory tuple for our current entry
                    // note: blacklisting does not tear down the current data source, it just prevents it from being used again
                    _sourcesList.Remove(_currentEntry);
                    _currentEntry = default;
                }
            });
        }

        public void MarkInitialized(bool status)
        {
            if (!_initialized.GetAndSet(true))
            {
                _initializedTask.TrySetResult(status);
            }
        }

        #endregion
    }
}
