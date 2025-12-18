using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    using FactoryList = List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>;


    internal static partial class FDv2DataSource
    {
        /// <summary>
        /// Creates a new FDv2 data source.
        /// </summary>
        /// <param name="updatesSink">the sink that receives updates from the active source</param>
        /// <param name="initializers">List of data source factories used for initialization</param>
        /// <param name="synchronizers">List of data source factories used for synchronization</param>
        /// <param name="fdv1Synchronizers">List of data source factories used for FDv1 synchronization if fallback to FDv1 occurs</param>
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

            ActionApplierFactory fdv1FallbackApplierFactory = (actionable) => new FDv1FallbackActionApplier(actionable);

            var initializationTracker =
                new InitializationTracker(Any(initializers), Any(synchronizers));
            var initializationObserver =
                new InitializationObserver(initializationTracker, DataSourceCategory.Initializers);
            var synchronizationObserver =
                new InitializationObserver(initializationTracker, DataSourceCategory.Synchronizers);
            var fallbackSynchronizationObserver =
                new InitializationObserver(initializationTracker, DataSourceCategory.FallbackSynchronizers);

            var underlyingComposites = new FactoryList();

            // Only create the initializers composite if initializers are provided
            if (initializers != null && initializers.Count > 0)
            {
                underlyingComposites.Add((
                    // Create the initializersCompositeSource with action logic unique to initializers
                    (sink) =>
                    {
                        var initializerFactory = new FactoryList();
                        for (int i = 0; i < initializers.Count; i++)
                        {
                            initializerFactory.Add((initializers[i],
                                fastFallbackApplierFactory));
                        }

                        // The common data source updates implements both IDataSourceUpdates and IDataSourceUpdatesV2.
                        return new CompositeSource(sink, initializerFactory, circular: false);
                    },
                    (actionable) => new CompositeObserver(
                        blacklistWhenSuccessOrOff(actionable), initializationObserver)
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
                            new FactoryList();
                        for (int i = 0; i < synchronizers.Count; i++)
                        {
                            synchronizersFactoryTuples.Add((synchronizers[i],
                                timedFallbackAndRecoveryApplierFactory));
                        }

                        return new CompositeSource(sink, synchronizersFactoryTuples);
                    },
                    (actionable) =>
                    {
                        // Only attach FDv1 fallback applier if FDv1 synchronizers are actually provided
                        if (fdv1Synchronizers != null && fdv1Synchronizers.Count > 0)
                        {
                            return new CompositeObserver(fdv1FallbackApplierFactory(actionable),
                                synchronizationObserver);
                        }

                        return synchronizationObserver;
                    }
                ));
            }

            // Add the FDv1 fallback synchronizers composite if provided
            if (fdv1Synchronizers != null && fdv1Synchronizers.Count > 0)
            {
                underlyingComposites.Add((
                    // Create fdv1SynchronizersCompositeSource with action logic unique to fdv1Synchronizers
                    (sink) =>
                    {
                        var fdv1SynchronizersFactoryTuples =
                            new FactoryList();
                        for (int i = 0; i < fdv1Synchronizers.Count; i++)
                        {
                            fdv1SynchronizersFactoryTuples.Add((fdv1Synchronizers[i],
                                timedFallbackAndRecoveryApplierFactory)); // fdv1 synchronizers behave same as synchronizers
                        }

                        return new CompositeSource(sink, fdv1SynchronizersFactoryTuples);
                    }, (applier) => fallbackSynchronizationObserver
                ));
            }

            return new CompletingDataSource(new CompositeSource(updatesSink, underlyingComposites, circular: false),
                initializationTracker);
        }

        /// <summary>
        /// Action applier for initializers that handles falling back to the next initializer when Interrupted and Off states is seen.
        /// </summary>
        private class ActionApplierFastFallback : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;

            public ActionApplierFastFallback(ICompositeSourceActionable actionable)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
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

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                if (!changeSet.Selector.IsEmpty)
                {
                    // If the selector is non-empty, then we will transition from initializers to synchronizers.
                    // If it is empty we attempt to move to the next initializer. If there are none, that will
                    // trigger an error, which will then initiate the transition.
                    return;
                }

                _actionable.DisposeCurrent();
                _actionable.GoToNext();
                _actionable.StartCurrent();
            }
        }

        /// <summary>
        /// Action applier for synchronizers that handles falling back to the next synchronizer when Interrupted and Off states is seen.
        /// </summary>
        private class ActionApplierTimedFallbackAndRecovery : IDataSourceObserver
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
                        if (newError != null && !newError.Value.Recoverable)
                        {
                            _actionable.BlacklistCurrent();
                        }
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

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                lock (_lock)
                {
                    CancelPendingFallbackTask();
                }
            }
        }

        /// <summary>
        /// Action applier that blacklists the current datasource when init occurs or when Off status is seen,
        /// then disposes the current datasource, goes to the next datasource, and starts it.
        /// </summary>
        private class ActionApplierBlacklistWhenSuccessOrOff : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;

            public ActionApplierBlacklistWhenSuccessOrOff(ICompositeSourceActionable actionable)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
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

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                // If this change has a selector, then we know we can move out of the current phase.
                // This doesn't look at the type of the changeset (Full, Partial, None), because having
                // a selector means that we have some payload. 
                // From a forward development perspective this could be because we had a local stale selector which was
                // persisted in some way, and we are getting up to date via an initializer.
                if (changeSet.Selector.IsEmpty) return;
                _actionable.BlacklistCurrent();
                _actionable.DisposeCurrent();
                _actionable.GoToNext();
                _actionable.StartCurrent();
            }
        }

        private class FDv1FallbackActionApplier : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;

            public FDv1FallbackActionApplier(ICompositeSourceActionable actionable)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                if (newError != null && newError.Value.FDv1Fallback)
                {
                    _actionable.BlacklistCurrent(); // blacklist the synchronizers altogether
                    _actionable.DisposeCurrent(); // dispose the synchronizers
                    _actionable.GoToNext(); // go to the FDv1 fallback synchronizer
                    _actionable.StartCurrent(); // start the FDv1 fallback synchronizer
                }
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                // this FDv1 fallback action applier doesn't care about apply, it only looks for the FDv1Fallback flag in the errors
            }
        }


        public enum DataSourceCategory
        {
            Initializers,
            Synchronizers,
            FallbackSynchronizers
        }

        /// <summary>
        /// Observes signals from underlying composites to determine if the data source is initialized.
        /// </summary>
        private class InitializationObserver : IDataSourceObserver
        {
            private readonly InitializationTracker _initializationTracker;
            private readonly DataSourceCategory _category;

            public InitializationObserver(InitializationTracker initializationTracker, DataSourceCategory category)
            {
                _initializationTracker = initializationTracker ??
                                         throw new ArgumentNullException(nameof(initializationTracker));
                _category = category;
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                _initializationTracker.Apply(changeSet, _category);
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                _initializationTracker.UpdateStatus(newState, newError, _category);
            }
        }

        private static bool Any<T>(params IList<T>[] items)
        {
            foreach (var item in items)
            {
                if (item != null && item.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
