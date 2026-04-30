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
            
            // Here we make a combined composite source, with the initializer source first which switches or falls back to the 
            // synchronizer source when the initializer succeeds or when the initializer source reports Off (all initializers failed)
            // Shared latch: once the FDv1 fallback applier has fired for any entry, all other
            // appliers attached at this level become no-ops. This prevents the existing
            // initializer-exhaustion or synchronizer-exhaustion appliers from firing afterwards
            // and blocking the FDv1 fallback entry that was just selected.
            var fdv1FallbackTriggered = new FDv1FallbackLatch();

            ActionApplierFactory blacklistWhenSuccessOrOff =
                (actionable) => new GatedObserver(
                    new ActionApplierBlacklistWhenSuccessOrOff(actionable), fdv1FallbackTriggered);
            ActionApplierFactory fastFallbackApplierFactory = (actionable) => new ActionApplierFastFallback(actionable);
            ActionApplierFactory timedFallbackAndRecoveryApplierFactory =
                (actionable) => new GatedObserver(
                    new ActionApplierTimedFallbackAndRecovery(actionable, sublogger), fdv1FallbackTriggered);

            // From the synchronizers entry, the FDv1 fallback entry is the next entry in the
            // outer list, so no extra entries to skip.
            ActionApplierFactory fdv1FallbackApplierFactory =
                (actionable) => new FDv1FallbackActionApplier(actionable, fdv1FallbackTriggered);
            // From the initializers entry, the FDv1 fallback entry is two ahead when synchronizers
            // are configured (skip past synchronizers), or one ahead when they are not.
            var initializerFdv1FallbackExtraSkips = (synchronizers != null && synchronizers.Count > 0) ? 1 : 0;
            ActionApplierFactory initializerFdv1FallbackApplierFactory =
                (actionable) => new FDv1FallbackActionApplier(actionable, fdv1FallbackTriggered, initializerFdv1FallbackExtraSkips);

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
                        return new CompositeSource("Initializers", sink, initializerFactory, sublogger, circular: false);
                    },
                    (actionable) =>
                    {
                        // Honor the FDv1 fallback directive in the initializer phase too. When the
                        // server returns x-ld-fd-fallback during init, skip the FDv2 synchronizer
                        // chain entirely and switch directly to the FDv1 fallback synchronizer.
                        if (fdv1Synchronizers != null && fdv1Synchronizers.Count > 0)
                        {
                            return new CompositeObserver(
                                initializationObserver,
                                blacklistWhenSuccessOrOff(actionable),
                                initializerFdv1FallbackApplierFactory(actionable));
                        }

                        return new CompositeObserver(
                            initializationObserver, blacklistWhenSuccessOrOff(actionable));
                    }
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
                    (actionable) =>
                    {
                        // Only attach FDv1 fallback applier if FDv1 synchronizers are actually provided
                        if (fdv1Synchronizers != null && fdv1Synchronizers.Count > 0)
                        {
                            return new CompositeObserver(synchronizationObserver, fdv1FallbackApplierFactory(actionable));
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

                        return new CompositeSource("FDv1FallbackSynchronizers", sink, fdv1SynchronizersFactoryTuples, sublogger);
                    }, (applier) => fallbackSynchronizationObserver
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
                    _actionable.BlockCurrent();
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
                _actionable.BlockCurrent();
                _actionable.DisposeCurrent();
                _actionable.GoToNext();
                _actionable.StartCurrent();
            }
        }

        /// <summary>
        /// Single-shot latch shared between the FDv1 fallback applier and the surrounding
        /// fallback/recovery appliers. Once the FDv1 fallback directive is observed at any entry,
        /// other appliers stop reacting -- they would otherwise observe the now-unwanted Off
        /// signals from previously running data sources and try to advance the composite again.
        /// </summary>
        internal sealed class FDv1FallbackLatch
        {
            private int _triggered;

            /// <summary>
            /// Returns whether the latch has already been triggered.
            /// </summary>
            public bool IsTriggered => System.Threading.Volatile.Read(ref _triggered) != 0;

            /// <summary>
            /// Atomically sets the latch. Returns true if this call was the one that set it.
            /// </summary>
            public bool TryTrigger() => System.Threading.Interlocked.CompareExchange(ref _triggered, 1, 0) == 0;
        }

        /// <summary>
        /// Wraps another observer and suppresses both Apply and UpdateStatus calls once the FDv1
        /// fallback latch has been triggered. The latch is set by <see cref="FDv1FallbackActionApplier"/>
        /// when it observes the FDv1 fallback directive.
        /// </summary>
        internal sealed class GatedObserver : IDataSourceObserver
        {
            private readonly IDataSourceObserver _inner;
            private readonly FDv1FallbackLatch _latch;

            public GatedObserver(IDataSourceObserver inner, FDv1FallbackLatch latch)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _latch = latch ?? throw new ArgumentNullException(nameof(latch));
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                if (_latch.IsTriggered) return;
                _inner.UpdateStatus(newState, newError);
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                if (_latch.IsTriggered) return;
                _inner.Apply(changeSet);
            }
        }

        /// <summary>
        /// Action applier that observes an FDv1 fallback signal and advances the outer composite
        /// to the FDv1 fallback synchronizer entry, blocking the current entry and any number of
        /// intermediate entries that should also be skipped.
        /// </summary>
        /// <remarks>
        /// When attached to the synchronizers entry of the outer FDv2 composite, the FDv1 fallback
        /// entry is the next one in the list, so <c>extraEntriesToSkip</c> is 0. When attached to
        /// the initializers entry and the synchronizers entry is also configured, we have to skip
        /// past it, so <c>extraEntriesToSkip</c> is 1.
        /// </remarks>
        internal class FDv1FallbackActionApplier : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;
            private readonly FDv1FallbackLatch _latch;
            private readonly int _extraEntriesToSkip;

            public FDv1FallbackActionApplier(ICompositeSourceActionable actionable, FDv1FallbackLatch latch = null, int extraEntriesToSkip = 0)
            {
                _actionable = actionable ?? throw new ArgumentNullException(nameof(actionable));
                if (extraEntriesToSkip < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(extraEntriesToSkip));
                }
                _latch = latch ?? new FDv1FallbackLatch();
                _extraEntriesToSkip = extraEntriesToSkip;
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                if (newError == null || !newError.Value.FDv1Fallback) return;
                if (!_latch.TryTrigger()) return;

                _actionable.BlockCurrent(); // blacklist the current entry
                _actionable.DisposeCurrent(); // dispose the current data source
                for (var i = 0; i < _extraEntriesToSkip; i++)
                {
                    _actionable.GoToNext();   // advance to the entry we are skipping past
                    _actionable.BlockCurrent(); // remove that entry from the list too
                }
                _actionable.GoToNext(); // go to the FDv1 fallback synchronizer entry
                _actionable.StartCurrent(); // start the FDv1 fallback synchronizer
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
