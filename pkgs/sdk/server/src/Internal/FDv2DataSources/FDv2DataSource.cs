using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    using FactoryList = List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>;
    using OuterFactoryList = List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory, CompositeEntryKind Kind)>;


    internal static partial class FDv2DataSource
    {
        /// <summary>
        /// Creates a new FDv2 data source.
        /// </summary>
        /// <param name="updatesSink">the sink that receives updates from the active source</param>
        /// <param name="initializers">List of data source factories used for initialization</param>
        /// <param name="synchronizers">List of data source factories used for synchronization</param>
        /// <param name="fdv1Synchronizers">List of data source factories used for FDv1 synchronization if fallback to FDv1 occurs</param>
        /// <param name="logger">the logger instance to use</param>
        /// <returns>a new data source instance</returns>
        public static IDataSource CreateFDv2DataSource(
            IDataSourceUpdatesV2 updatesSink,
            IList<SourceFactory> initializers,
            IList<SourceFactory> synchronizers,
            IList<SourceFactory> fdv1Synchronizers,
            Logger logger)
        {
            var sublogger = logger.SubLogger(LogNames.FDv2DataSourceSubLog);

            // The flow-control appliers (fast-fallback, blacklist, timed-fallback-and-recovery)
            // each watch for the FDv1 directive on their inputs and bail when it is set, so the
            // FDv1 fallback applier owns the transition uncontested. The directive arrives either
            // on UpdateStatus (errorInfo.FDv1Fallback) or on Apply (changeSet.FDv1Fallback) when
            // the response was a successful FDv2 payload that also carried the directive header.
            ActionApplierFactory blacklistWhenSuccessOrOff =
                (actionable) => new ActionApplierBlacklistWhenSuccessOrOff(actionable);
            ActionApplierFactory fastFallbackApplierFactory = (actionable) => new ActionApplierFastFallback(actionable);
            ActionApplierFactory timedFallbackAndRecoveryApplierFactory =
                (actionable) => new ActionApplierTimedFallbackAndRecovery(actionable, sublogger);

            // The FDv1 fallback applier is phase-agnostic: when the directive is observed it
            // calls BlockAll(FDv2) to remove every FDv2 entry from the outer list, then GoToNext
            // lands on the FDv1 fallback entry (or on exhaustion when none was configured).
            ActionApplierFactory fdv1FallbackApplierFactory =
                (actionable) => new FDv1FallbackActionApplier(actionable);

            var initializationTracker =
                new InitializationTracker(Any(initializers), Any(synchronizers));
            var initializationObserver =
                new InitializationObserver(initializationTracker, DataSourceCategory.Initializers);
            var synchronizationObserver =
                new InitializationObserver(initializationTracker, DataSourceCategory.Synchronizers);
            var fallbackSynchronizationObserver =
                new InitializationObserver(initializationTracker, DataSourceCategory.FallbackSynchronizers);

            var underlyingComposites = new OuterFactoryList();

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
                        return new CompositeSource("Initializers", sink, initializerFactory, sublogger, circular: false);
                    },
                    (actionable) => new CompositeObserver(
                        initializationObserver,
                        fdv1FallbackApplierFactory(actionable),
                        blacklistWhenSuccessOrOff(actionable)),
                    CompositeEntryKind.FDv2
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

                        return new CompositeSource("Synchronizers", sink, synchronizersFactoryTuples, sublogger);
                    },
                    (actionable) => new CompositeObserver(synchronizationObserver, fdv1FallbackApplierFactory(actionable)),
                    CompositeEntryKind.FDv2
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

                        return new CompositeSource("FDv1FallbackSynchronizers", sink, fdv1SynchronizersFactoryTuples, sublogger);
                    },
                    (applier) => fallbackSynchronizationObserver,
                    CompositeEntryKind.FDv1Fallback
                ));
            }

            return new CompletingDataSource(new CompositeSource("FDv2DataSource", updatesSink, underlyingComposites, sublogger, circular: false),
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
                // When the FDv1 directive rides on the status, the FDv1 fallback applier owns the
                // transition. Bail so we don't double-advance the inner initializers list.
                if (newError?.FDv1Fallback == true) return;

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

                // Empty selector with directive: still defer to the FDv1 fallback applier.
                if (changeSet.FDv1Fallback) return;

                _actionable.DisposeCurrent();
                _actionable.GoToNext();
                _actionable.StartCurrent();
            }
        }

        /// <summary>
        /// Action applier for synchronizers that handles falling back to the next synchronizer when Interrupted and Off states is seen.
        /// </summary>
        internal class ActionApplierTimedFallbackAndRecovery : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;
            private readonly Logger _log;
            private readonly object _lock = new object();
            private Task _fallbackTask;
            private CancellationTokenSource _fallbackCanceller;
            private Task _recoveryTask;
            private CancellationTokenSource _recoveryCanceller;
            private readonly TimeSpan _interruptedFallbackTimeout;
            private readonly TimeSpan _validRecoveryTimeout;
            private static readonly TimeSpan DefaultInterruptedFallbackTimeout = TimeSpan.FromMinutes(2);
            private static readonly TimeSpan DefaultValidRecoveryTimeout = TimeSpan.FromMinutes(5);

            public ActionApplierTimedFallbackAndRecovery(ICompositeSourceActionable actionable, Logger logger)
                : this(actionable, logger, DefaultInterruptedFallbackTimeout, DefaultValidRecoveryTimeout)
            {
            }

            internal ActionApplierTimedFallbackAndRecovery(ICompositeSourceActionable actionable, Logger logger, TimeSpan interruptedFallbackTimeout, TimeSpan validRecoveryTimeout)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
                _log = logger ?? throw new ArgumentNullException(nameof(logger));
                _interruptedFallbackTimeout = interruptedFallbackTimeout;
                _validRecoveryTimeout = validRecoveryTimeout;
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                // When the FDv1 directive rides on the status, the FDv1 fallback applier owns the
                // transition. Bail before scheduling timers or advancing within the inner sync
                // list -- otherwise we'd briefly start the next sync before the outer composite
                // disposes us.
                if (newError?.FDv1Fallback == true)
                {
                    return;
                }

                lock (_lock)
                {
                    // If there's a pending fallback task and status is not Interrupted, cancel it
                    if (_fallbackTask != null && newState != DataSourceState.Interrupted)
                    {
                        CancelPendingFallbackTask();
                    }

                    // If there's a pending recovery task and status is not Valid, cancel it
                    if (_recoveryTask != null && newState != DataSourceState.Valid)
                    {
                        CancelPendingRecoveryTask();
                    }

                    // When a synchronizer reports it is off, fall back immediately
                    if (newState == DataSourceState.Off)
                    {
                        if (newError != null && !newError.Value.Recoverable)
                        {
                            _actionable.BlockCurrent();
                        }
                        _actionable.DisposeCurrent();
                        _actionable.GoToNext();
                        _actionable.StartCurrent();
                        return;
                    }

                    // If status is Interrupted, schedule a fallback task if not already scheduled
                    if (_fallbackTask == null && newState == DataSourceState.Interrupted)
                    {
                        _fallbackCanceller = new CancellationTokenSource();
                        var cancellationToken = _fallbackCanceller.Token;

                        // Schedule a task to check after 2 minutes if status is still Interrupted
                        _fallbackTask = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(_interruptedFallbackTimeout, cancellationToken);

                                // If we reach here, the task wasn't cancelled during the delay
                                // But we need to check again inside the lock to handle race conditions
                                lock (_lock)
                                {
                                    // If _interruptedFallbackTask is null, the task was cancelled after the delay completed
                                    // Also check if the token was cancelled as an additional safety check
                                    if (_fallbackTask == null || cancellationToken.IsCancellationRequested)
                                    {
                                        return;
                                    }

                                    // Clean up
                                    _fallbackCanceller?.Dispose();
                                    _fallbackCanceller = null;
                                    _fallbackTask = null;

                                    _log.Warn("Current data source has been interrupted for more than {0} minutes, falling back to next source.", _interruptedFallbackTimeout.TotalMinutes);

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
                                    _fallbackTask = null;
                                }
                                // The CancellationTokenSource will be disposed by CancelPendingFallbackTask
                            }
                        }, cancellationToken);
                    }

                    // If we are not at the first of the underlying sources and status is Valid, schedule a recovery task if not already scheduled
                    if (_recoveryTask == null && !_actionable.IsAtFirst() && newState == DataSourceState.Valid)
                    {
                        _recoveryCanceller = new CancellationTokenSource();
                        var cancellationToken = _recoveryCanceller.Token;

                        // Schedule a task to check after 5 minutes if status is still Valid
                        _recoveryTask = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(_validRecoveryTimeout, cancellationToken);

                                // If we reach here, the task wasn't cancelled during the delay
                                // But we need to check again inside the lock to handle race conditions
                                lock (_lock)
                                {
                                    // If _validRecoveryTask is null, the task was cancelled after the delay completed
                                    // Also check if the token was cancelled as an additional safety check
                                    if (_recoveryTask == null || cancellationToken.IsCancellationRequested)
                                    {
                                        return;
                                    }

                                    // Clean up
                                    _recoveryCanceller?.Dispose();
                                    _recoveryCanceller = null;
                                    _recoveryTask = null;

                                    _log.Info("Current data source has been valid for more than {0} minutes, recovering to primary source.", _validRecoveryTimeout.TotalMinutes);

                                    // Do the recovery: dispose current, go to first, start current
                                    _actionable.DisposeCurrent();
                                    _actionable.GoToFirst();
                                    _actionable.StartCurrent();
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Task was cancelled, which is expected if status changed
                                // Clean up the task reference
                                lock (_lock)
                                {
                                    _recoveryTask = null;
                                }
                                // The CancellationTokenSource will be disposed by CancelPendingRecoveryTask
                            }
                        }, cancellationToken);
                    }
                }
            }

            private void CancelPendingFallbackTask()
            {
                if (_fallbackCanceller != null)
                {
                    _fallbackCanceller.Cancel();
                    _fallbackCanceller.Dispose();
                    _fallbackCanceller = null;
                }

                _fallbackTask = null;
            }

            private void CancelPendingRecoveryTask()
            {
                if (_recoveryCanceller != null)
                {
                    _recoveryCanceller.Cancel();
                    _recoveryCanceller.Dispose();
                    _recoveryCanceller = null;
                }

                _recoveryTask = null;
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                // apply does nothing wrt fallback and recovery, only status matters
            }
        }

        /// <summary>
        /// Action applier that blacklists the current datasource when init occurs or when Off status is seen,
        /// then disposes the current datasource, goes to the next datasource, and starts it.
        /// </summary>
        /// <remarks>
        /// When the FDv1 directive rides on the input (changeset or error info), this applier
        /// bails so the FDv1 fallback applier can drive the transition uncontested.
        /// </remarks>
        internal class ActionApplierBlacklistWhenSuccessOrOff : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;

            public ActionApplierBlacklistWhenSuccessOrOff(ICompositeSourceActionable actionable)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                if (newState != DataSourceState.Off) return;
                if (newError?.FDv1Fallback == true) return;

                _actionable.BlockCurrent();
                _actionable.DisposeCurrent();
                _actionable.GoToNext();
                _actionable.StartCurrent();
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                // If this change has a selector, then we know we can move out of the current phase.
                // This doesn't look at the type of the changeset (Full, Partial, None), because having
                // a selector means that we have some payload.
                // From a forward development perspective this could be because we had a local stale selector which was
                // persisted in some way, and we are getting up to date via an initializer.
                if (changeSet.Selector.IsEmpty) return;
                if (changeSet.FDv1Fallback) return;

                _actionable.BlockCurrent();
                _actionable.DisposeCurrent();
                _actionable.GoToNext();
                _actionable.StartCurrent();
            }
        }

        /// <summary>
        /// Action applier that observes an FDv1 fallback signal and advances the outer composite
        /// to the FDv1 fallback synchronizer entry. The directive may arrive either on
        /// <see cref="UpdateStatus"/> (errorInfo.FDv1Fallback) or on <see cref="Apply"/>
        /// (changeSet.FDv1Fallback) when a successful payload also carried the directive header.
        /// </summary>
        /// <remarks>
        /// Phase-agnostic: when attached to either the initializers entry or the synchronizers
        /// entry of the outer FDv2 composite, <see cref="ICompositeSourceActionable.BlockAll"/>
        /// removes every FDv2 entry in one shot, leaving the FDv1 fallback entry (if configured)
        /// as the next stop. When no FDv1 fallback entry was configured the outer list is exhausted
        /// and the composite halts the data system.
        ///
        /// The single-shot _triggered flag makes this applier idempotent in case the directive
        /// arrives more than once on the same propagation chain (for example, on a successful FDv2
        /// response the source emits Apply with the flag and then UpdateStatus(Off, FDv1Fallback)
        /// during shutdown).
        /// </remarks>
        internal class FDv1FallbackActionApplier : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;
            private int _triggered;

            public FDv1FallbackActionApplier(ICompositeSourceActionable actionable)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                if (newError?.FDv1Fallback != true) return;
                Trigger();
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                if (!changeSet.FDv1Fallback) return;
                Trigger();
            }

            private void Trigger()
            {
                if (Interlocked.CompareExchange(ref _triggered, 1, 0) != 0) return;

                _actionable.BlockAll(kind => kind == CompositeEntryKind.FDv2);
                _actionable.GoToNext();
                _actionable.StartCurrent();
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
