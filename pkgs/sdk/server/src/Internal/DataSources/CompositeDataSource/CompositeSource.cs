using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// A composite source is a source that can dynamically switch between sources with the
    /// help of a list of <see cref="ISourceFactory"/> instances.
    /// </summary>
    internal sealed class CompositeSource : IDataSource, ICompositeSourceActionable
    {
        // All mutable state and the internal action queue are protected by this lock.
        // We also use a small, non-recursive action queue so that any re-entrant calls
        // from IActionApplier logic are serialized and processed iteratively instead
        // of recursively, avoiding the risk of stack overflows.
        private readonly object _lock = new object();
        private readonly Queue<QueuedAction> _pendingActions = new Queue<QueuedAction>();
        private bool _isProcessingActions;

        /// <summary>
        /// Wraps an action with the data source that was current when it was enqueued.
        /// This allows us to ignore stale actions that were queued for a different data source.
        /// </summary>
        private sealed class QueuedAction
        {
            public readonly Action Action;
            public readonly IDataSource DataSourceAtEnqueueTime;

            public QueuedAction(Action action, IDataSource dataSourceAtEnqueueTime)
            {
                Action = action ?? throw new ArgumentNullException(nameof(action));
                DataSourceAtEnqueueTime = dataSourceAtEnqueueTime;
            }
        }

        private readonly IDataSourceUpdates _sanitizedUpdateSink;
        private readonly SourcesList<(ISourceFactory Factory, IActionApplierFactory ActionApplierFactory)> _sourcesList;
        private readonly DisableableDataSourceUpdatesTracker _disableableTracker;

        // Tracks the entry from the sources list that was used to create the current
        // data source instance. This allows operations such as blacklist to remove
        // the correct factory/action-applier-factory tuple from the list.
        private (ISourceFactory Factory, IActionApplierFactory ActionApplierFactory) _currentEntry;
        private IDataSource _currentDataSource;

        /// <summary>
        /// Creates a new <see cref="CompositeSource"/>.
        /// </summary>
        /// <param name="updatesSink">the sink that receives updates from the active source</param>
        /// <param name="factoryTuples">the ordered list of source factories and their associated action applier factories</param>
        /// <param name="circular">whether to loop off the end of the list back to the start when fallback occurs</param>
        public CompositeSource(
            IDataSourceUpdates updatesSink,
            IList<(ISourceFactory Factory, IActionApplierFactory ActionApplierFactory)> factoryTuples,
            bool circular = true)
        {
            _sanitizedUpdateSink = new DataSourceUpdatesSanitizer(updatesSink) ?? throw new ArgumentNullException(nameof(updatesSink));
            if (factoryTuples is null)
            {
                throw new ArgumentNullException(nameof(factoryTuples));
            }

            // this tracker is used to disconnect the current source from the updates sink when it is no longer needed.
            _disableableTracker = new DisableableDataSourceUpdatesTracker();

            _sourcesList = new SourcesList<(ISourceFactory SourceFactory, IActionApplierFactory ActionApplierFactory)>(
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
            return StartCurrent();
        }

        /// <summary>
        /// Returns whether the current underlying data source has finished initializing.
        /// </summary>
        public bool Initialized => _currentDataSource?.Initialized ?? false;

        /// <summary>
        /// Disposes of the composite data source. This should only be called once.
        /// </summary>
        public void Dispose()
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

                // report state Off
                _sanitizedUpdateSink.UpdateStatus(DataSourceState.Off, null);

                // clear any queued actions and reset processing state
                _pendingActions.Clear();
                _isProcessingActions = false;
                _sourcesList.Reset();
                _currentEntry = default;
            }
        }

        /// <summary>
        /// Enqueue a state-changing operation to be executed under the shared lock.
        /// If no other operation is currently running, this will synchronously process
        /// the queue in a simple loop on the current thread. Any re-entrant calls from
        /// within the operations will only enqueue more work; they will not trigger
        /// another processing loop, so the call stack does not grow with the queue length.
        /// 
        /// The current data source is captured at enqueue time. When the action is executed,
        /// it will only run if the current data source is still the same one.
        /// </summary>
        private void EnqueueAction(Action action)
        {
            bool shouldProcess = false;
            IDataSource dataSourceAtEnqueueTime;
            lock (_lock)
            {
                // Capture the current data source reference at the time of enqueueing
                dataSourceAtEnqueueTime = _currentDataSource;
                _pendingActions.Enqueue(new QueuedAction(action, dataSourceAtEnqueueTime));
                if (!_isProcessingActions)
                {
                    _isProcessingActions = true;
                    shouldProcess = true;
                }
            }

            if (shouldProcess)
            {
                ProcessQueuedActions();
            }
        }

        private void ProcessQueuedActions()
        {
            while (true)
            {
                QueuedAction queuedAction;
                bool shouldExecute;
                lock (_lock)
                {
                    if (_pendingActions.Count == 0)
                    {
                        _isProcessingActions = false;
                        return;
                    }

                    queuedAction = _pendingActions.Dequeue();

                    // Check if the data source is still the same as when the action was enqueued.
                    // If it has changed, the action is stale and should be ignored.
                    shouldExecute = ReferenceEquals(_currentDataSource, queuedAction.DataSourceAtEnqueueTime);
                }

                if (!shouldExecute)
                {
                    // Data source has changed since this action was enqueued; skip it.
                    continue;
                }

                // Execute outside of the lock so that operations can do more work,
                // including calling back into the composite and queuing additional
                // actions without blocking the queue.
                queuedAction.Action();
            }
        }

        private void TryFindNextUnderLock()
        {
            // This method must only be called while holding _lock.
            if (_currentDataSource != null)
            {
                return;
            }

            var entry = _sourcesList.Next();
            if (entry.Factory == null)
            {
                return;
            }

            var actionApplier = entry.ActionApplierFactory.CreateActionApplier(this);

            // here we make a fanout so that we can trigger actions as well as forward calls to the sanitized sink (order matters here)
            var fanout = new FanOutDataSourceUpdates(new List<IDataSourceUpdates> { actionApplier, _sanitizedUpdateSink });
            var disableableUpdates = _disableableTracker.WrapAndTrack(fanout);
            
            _currentEntry = entry;
            _currentDataSource = entry.Factory.CreateSource(disableableUpdates);
        }

        #region ICompositeSourceActionable

        /// <summary>
        /// Starts the current data source.
        /// </summary>
        public Task<bool> StartCurrent()
        {
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
                dataSourceToStart.Start().ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        tcs.TrySetException(task.Exception);
                    }
                    else if (task.IsCanceled)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(task.Result);
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            });

            return tcs.Task;
        }

        /// <summary>
        /// Disposes of the current data source. You must call GoToNext or GoToFirst after this to change to a new factory.
        /// </summary>
        public void DisposeCurrent()
        {
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

        #endregion
    }
}


