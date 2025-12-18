using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    internal static partial class FDv2DataSource
    {
        /// <summary>
        /// Ingests observations from several composite data sources and combines the observed state into an
        /// initialization state.
        /// <para>
        /// This tracker uses a strategy where it attempts to get the best possible data. We prioritize getting data
        /// that includes a selector over data which does not. If no data with a selector is available, but data
        /// without a selector is available, and we have exhausted all initializers, then we will consider ourselves
        /// initialized.
        /// </para>
        /// <para>
        /// In the case where there isn't any potential to get data to initialize, for instance, no initializers and
        /// no synchronizers, then we assume we are initialized. (offline or daemon mode).
        /// </para>
        /// </summary>
        private class InitializationTracker : IDisposable
        {
            private readonly TaskCompletionSource<bool> _taskCompletionSource = new TaskCompletionSource<bool>();
            private State _state = State.NoData;

            private bool _initializersRemain;
            private bool _synchronizersRemain;
            private bool _fallbackRemain;

            private enum State
            {
                /// <summary>
                /// The tracker has not received any data.
                /// </summary>
                NoData,

                /// <summary>
                /// The tracker has received any data.
                /// </summary>
                Data,

                /// <summary>
                /// The tracker has been informed that initializers are exhausted.
                /// </summary>
                InitializersExhausted,

                /// <summary>
                /// The tracker is initialized and is no longer processing updates.
                /// </summary>
                Initialized,

                /// <summary>
                /// The tracker has encountered a total failure.
                /// </summary>
                Failed,
            }

            private enum Action
            {
                /// <summary>
                /// We have received some data.
                /// </summary>
                DataReceived,

                /// <summary>
                /// We have received signals that indicate initializers are exhausted.
                /// </summary>
                InitializersExhausted,

                /// <summary>
                /// We have received signals that indicate synchronizers are exhausted.
                /// </summary>
                SynchronizersExhausted,

                /// <summary>
                /// We have received signals that indicate all fallbacks are exhausted.
                /// </summary>
                FallbackExhausted,

                /// <summary>
                /// We have received a selector.
                /// </summary>
                SelectorReceived,
            }

            public InitializationTracker(bool hasInitializers, bool hasSynchronizers, bool hasFallback)
            {
                if (!(hasInitializers || hasSynchronizers || hasFallback))
                {
                    // If we have no data sources, then we are immediately initialized.
                    _state = State.Initialized;
                    _taskCompletionSource.TrySetResult(true);
                    return;
                }

                _initializersRemain = hasInitializers;
                _synchronizersRemain = hasSynchronizers;
                _fallbackRemain = hasFallback;

                if (!hasInitializers)
                {
                    DetermineState(Action.InitializersExhausted);
                }
            }

            public Task<bool> Task => _taskCompletionSource.Task;

            private bool RemainingSources => _initializersRemain || _synchronizersRemain || _fallbackRemain;

            private void HandleRemainingSources()
            {
                if (!RemainingSources)
                {
                    _state = State.Failed;
                }
            }

            private void DetermineState(Action action)
            {
                switch (_state)
                {
                    // Terminal states, ignore subsequent actions.
                    case State.Initialized:
                    case State.Failed:
                        break;
                    case State.NoData:
                        switch (action)
                        {
                            case Action.DataReceived:
                                _state = State.Data;
                                break;
                            case Action.InitializersExhausted:
                                _initializersRemain = false;
                                _state = State.InitializersExhausted;
                                HandleRemainingSources();
                                break;
                            case Action.SelectorReceived:
                                _state = State.Initialized;
                                break;
                            case Action.SynchronizersExhausted:
                                _synchronizersRemain = false;
                                HandleRemainingSources();
                                break;
                            case Action.FallbackExhausted:
                                _fallbackRemain = false;
                                HandleRemainingSources();
                                break;
                        }

                        break;
                    case State.Data:
                        switch (action)
                        {
                            case Action.DataReceived:
                                break;
                            case Action.SynchronizersExhausted:
                            case Action.FallbackExhausted:
                            case Action.InitializersExhausted:
                            case Action.SelectorReceived:
                                _state = State.Initialized;
                                break;
                        }

                        break;
                    case State.InitializersExhausted:
                        switch (action)
                        {
                            case Action.InitializersExhausted:
                                break;
                            case Action.SynchronizersExhausted:
                                _synchronizersRemain = false;
                                HandleRemainingSources();
                                break;
                            case Action.FallbackExhausted:
                                _fallbackRemain = false;
                                HandleRemainingSources();
                                break;
                            case Action.DataReceived:
                            case Action.SelectorReceived:
                                _state = State.Initialized;
                                break;
                        }

                        break;
                }

                // After updating the state determine if we need to complete the task.
                switch (_state)
                {
                    case State.Initialized:
                        _taskCompletionSource.TrySetResult(true);
                        break;
                    case State.Failed:
                        _taskCompletionSource.TrySetResult(false);
                        break;
                }
            }

            public void Apply(DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> changeSet, bool exhausted,
                DataSourceCategory category)
            {
                if (!changeSet.Selector.IsEmpty) DetermineState(Action.SelectorReceived);

                DetermineState(Action.DataReceived);
                if (category == DataSourceCategory.Initializers && exhausted)
                {
                    DetermineState(Action.InitializersExhausted);
                }
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError,
                DataSourceCategory category)
            {
                switch (category)
                {
                    case DataSourceCategory.Initializers when newState == DataSourceState.Off:
                    {
                        DetermineState(Action.InitializersExhausted);
                        break;
                    }
                    case DataSourceCategory.Synchronizers when newState == DataSourceState.Off:
                    {
                        DetermineState(Action.SynchronizersExhausted);
                        break;
                    }
                    case DataSourceCategory.FallbackSynchronizers when newState == DataSourceState.Off:
                    {
                        DetermineState(Action.FallbackExhausted);
                        break;
                    }
                    default:
                        break;
                }
            }

            public void Dispose()
            {
                _taskCompletionSource.TrySetResult(false);
            }
        }
    }
}
