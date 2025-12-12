using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    internal static class FDv2DataSource
    {
        /// <summary>
        /// Creates a new FDv2 data source.
        /// </summary>
        /// <param name="updatesSink">the sink that receives updates from the active source</param>
        /// <param name="initializers">List of data source factories used for initialization</param>
        /// <param name="synchronizers">List of data source factories used for synchronization</param>
        /// <param name="fdv1Synchronizers">List of data source factories used for FDv1 synchronization</param>
        /// <returns>a new data source instance</returns>
        public static IDataSource CreateFDv2DataSource(
            IDataSourceUpdatesV2 updatesSink,
            IList<SourceFactory> initializers,
            IList<SourceFactory> synchronizers,
            IList<SourceFactory> fdv1Synchronizers)
        {
            // Here we make a combined composite source, with the initializer source first which switches or falls back to the 
            // synchronizer source when the initializer succeeds or when the initializer source reports Off (all initializers failed)
            ActionApplierFactory blacklistWhenSuccessOrOff =
                (actionable) => new ActionApplierBlacklistWhenSuccessOrOff(actionable);
            ActionApplierFactory fastFallbackApplierFactory = (actionable) => new ActionApplierFastFallback(actionable);
            ActionApplierFactory timedFallbackAndRecoveryApplierFactory =
                (actionable) => new ActionApplierTimedFallbackAndRecovery(actionable);

            var underlyingComposites = new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>();

            // Only create the initializers composite if initializers are provided
            if (initializers != null && initializers.Count > 0)
            {
                underlyingComposites.Add((
                    // Create the initializersCompositeSource with action logic unique to initializers
                    (sink) =>
                    {
                        var initializersFactoryTuples =
                            new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>();
                        for (int i = 0; i < initializers.Count; i++)
                        {
                            initializersFactoryTuples.Add((initializers[i], fastFallbackApplierFactory));
                        }

                        return new CompositeSource(sink, initializersFactoryTuples, circular: false);
                    },
                    blacklistWhenSuccessOrOff
                ));
            }

            // Only create the synchronizers composite if synchronizers are provided
            if (synchronizers != null && synchronizers.Count > 0)
            {
                underlyingComposites.Add((
                    // Create synchronizersCompositeSource with action logic unique to synchronizers
                    (sink) =>
                    {
                        var synchronizersFactoryTuples =
                            new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>();
                        for (int i = 0; i < synchronizers.Count; i++)
                        {
                            synchronizersFactoryTuples.Add((synchronizers[i], timedFallbackAndRecoveryApplierFactory));
                        }

                        return new CompositeSource(sink, synchronizersFactoryTuples);
                    },
                    null // TODO: add fallback to FDv1 logic, null for the moment as once we're on the synchronizers, we stay there
                ));
            }

            var combinedCompositeSource = new CompositeSource(updatesSink, underlyingComposites, circular: false);

            // TODO: add fallback to FDv1 logic

            return combinedCompositeSource;
        }

        /// <summary>
        /// Action applier for initializers that handles falling back to the next initializer when Interrupted and Off states is seen.
        /// </summary>
        private class ActionApplierFastFallback : IActionApplier
        {
            private readonly ICompositeSourceActionable _actionable;

            public ActionApplierFastFallback(ICompositeSourceActionable actionable)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
            }

            // TODO: need to fix default
            public IDataStoreStatusProvider DataStoreStatusProvider => default;

            public bool Init(FullDataSet<ItemDescriptor> allData)
            {
                // this layer doesn't react to init
                return true; 
            }

            public bool Upsert(DataKind kind, string key, ItemDescriptor item)
            {
                // this layer doesn't react to upserts
                return true;
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                // when an initializer has an issue, fall back
                if (newState == DataSourceState.Interrupted || newState == DataSourceState.Off || newError != null)
                {
                    _actionable.DisposeCurrent();
                    _actionable.GoToNext();
                    _actionable.StartCurrent();
                }
            }

            public bool Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                // this layer doesn't react to apply
                return true;
            }

            public bool InitWithHeaders(FullDataSet<ItemDescriptor> allData, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
            {
                // this layer doesn't react to apply
                return true;
            }
        }

        /// <summary>
        /// Action applier for synchronizers that handles falling back to the next synchronizer when Interrupted and Off states is seen.
        /// </summary>
        private class ActionApplierTimedFallbackAndRecovery : IActionApplier
        {
            private readonly ICompositeSourceActionable _actionable;
            private readonly object _lock = new object();
            private Task _interruptedFallbackTask;
            private CancellationTokenSource _interruptedFallbackCanceller;
            private static readonly TimeSpan InterruptedFallbackTimeout = TimeSpan.FromMinutes(2);

            public ActionApplierTimedFallbackAndRecovery(ICompositeSourceActionable actionable)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
            }

            // TODO: need to fix default
            public IDataStoreStatusProvider DataStoreStatusProvider => default;

            public bool Init(FullDataSet<ItemDescriptor> allData)
            {
                return HandleSuccessCriteria();
            }

            public bool Upsert(DataKind kind, string key, ItemDescriptor item)
            {
                // this layer doesn't react to upserts
                return true;
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                lock (_lock)
                {
                    // If there's a pending fallback task and status is not Interrupted, cancel it
                    if (_interruptedFallbackTask != null && newState != DataSourceState.Interrupted)
                    {
                        CancelPendingFallbackTask();
                    }

                    // When a synchronizer reports it is off, fall back immediately
                    if (newState == DataSourceState.Off)
                    {
                        _actionable.DisposeCurrent();
                        _actionable.GoToNext();
                        _actionable.StartCurrent();
                        return;
                    }

                    // If status is Interrupted, schedule a fallback task if not already scheduled
                    if (_interruptedFallbackTask == null && newState == DataSourceState.Interrupted)
                    {
                        _interruptedFallbackCanceller = new CancellationTokenSource();
                        var cancellationToken = _interruptedFallbackCanceller.Token;

                        // Schedule a task to check after 2 minutes if status is still Interrupted
                        _interruptedFallbackTask = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(InterruptedFallbackTimeout, cancellationToken);

                                // If we reach here, the task wasn't cancelled during the delay
                                // But we need to check again inside the lock to handle race conditions
                                lock (_lock)
                                {
                                    // If _interruptedFallbackTask is null, the task was cancelled after the delay completed
                                    // Also check if the token was cancelled as an additional safety check
                                    if (_interruptedFallbackTask == null || cancellationToken.IsCancellationRequested)
                                    {
                                        return;
                                    }

                                    // Clean up
                                    _interruptedFallbackCanceller?.Dispose();
                                    _interruptedFallbackCanceller = null;
                                    _interruptedFallbackTask = null;

                                    // Do the fallback: dispose current, go to next, start current
                                    _actionable.DisposeCurrent();
                                    _actionable.GoToNext();
                                    _actionable.StartCurrent();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Task was cancelled, which is expected if status changed
                                // Clean up the task reference
                                lock (_lock)
                                {
                                    _interruptedFallbackTask = null;
                                }
                                // The CancellationTokenSource will be disposed by CancelPendingFallbackTask
                            }
                        }, cancellationToken);
                    }
                }
            }

            private void CancelPendingFallbackTask()
            {
                if (_interruptedFallbackCanceller != null)
                {
                    _interruptedFallbackCanceller.Cancel();
                    _interruptedFallbackCanceller.Dispose();
                    _interruptedFallbackCanceller = null;
                }

                _interruptedFallbackTask = null;
            }

            public bool Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                return HandleSuccessCriteria();
            }

            private bool HandleSuccessCriteria()
            {
                lock (_lock)
                {
                    CancelPendingFallbackTask();
                }

                // this layer doesn't react to upserts
                return true;
            }

            public bool InitWithHeaders(FullDataSet<ItemDescriptor> allData, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
            {
                return HandleSuccessCriteria();
            }
        }

        /// <summary>
        /// Action applier that blacklists the current datasource when init occurs or when Off status is seen,
        /// then disposes the current datasource, goes to the next datasource, and starts it.
        /// </summary>
        private class ActionApplierBlacklistWhenSuccessOrOff : IActionApplier
        {
            private readonly ICompositeSourceActionable _actionable;

            public ActionApplierBlacklistWhenSuccessOrOff(ICompositeSourceActionable actionable)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
            }

            // TODO: need to fix default
            public IDataStoreStatusProvider DataStoreStatusProvider => default;

            public bool Init(FullDataSet<ItemDescriptor> allData)
            {
                return SuccessTransition();
            }

            private bool SuccessTransition()
            {
                // TODO: Selector logic, early exit?
                _actionable.BlacklistCurrent();
                _actionable.DisposeCurrent();
                _actionable.GoToNext();
                _actionable.StartCurrent();
                return true;
            }

            public bool Upsert(DataKind kind, string key, ItemDescriptor item)
            {
                // this layer doesn't react to upserts
                return true;
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                // When Off status is seen, blacklist current, dispose current, go to next, and start current
                if (newState == DataSourceState.Off)
                {
                    _actionable.BlacklistCurrent();
                    _actionable.DisposeCurrent();
                    _actionable.GoToNext();
                    _actionable.StartCurrent();
                }
            }

            public bool Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                return SuccessTransition();
            }

            public bool InitWithHeaders(FullDataSet<ItemDescriptor> allData, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
            {
                // When init occurs, blacklist current, dispose current, go to next, and start current
                return SuccessTransition();
            }
        }
    }
}
