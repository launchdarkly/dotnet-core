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
        /// <para>
        /// State Machine:
        /// <code>
        /// </code>
        /// ┌───────────────────────┐              ┌────────────────────────────────────────┐
        /// │                       │              │                                        │
        /// │         NoData        ├DataReceived─►│                  Data                  │
        /// │                       │              │                                        │
        /// └───────────┬───────────┘              └────────────────────┬───────────────────┘
        ///             │                                               │                    
        ///             │                            InitializersExhausted/SelectorReceived  
        ///     SelectorReceived                                        ▼                      
        ///             │                          ┌────────────────────────────────────────┐
        ///             │                          │                                        │
        ///             ├─────────────────────────►│              Initialized               │
        ///   InitializersExhausted                │                                        │
        ///             │                          └────────────────────────────────────────┘
        ///             │                                               ▲                    
        ///             │                                 DataReceived/SelectorReceived      
        ///             │                                               │                    
        ///             │                          ┌────────────────────┴───────────────────┐
        ///             │                          │                                        │
        ///             └─────────────────────────►│         InitializersExhausted          │
        ///                                        │                                        │
        ///                                        └────────────────────────────────────────┘
        /// </para>
        /// </summary>
        private class InitializationTracker: IDisposable
        {
            private readonly TaskCompletionSource<bool> _taskCompletionSource = new TaskCompletionSource<bool>();
            private State _state = State.NoData;

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
                Initialized
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
                /// We have received a selector.
                /// </summary>
                SelectorReceived
            }

            public InitializationTracker(bool hasDataSources)
            {
                if (hasDataSources) return;
                // If we have no data sources, then we are immediately initialized.
                _state = State.Initialized;
                _taskCompletionSource.TrySetResult(true);
            }

            public Task<bool> Task => _taskCompletionSource.Task;

            private void DetermineState(Action action)
            {
                switch (_state)
                {
                    case State.Initialized:
                        break;
                    case State.NoData:
                        switch (action)
                        {
                            case Action.DataReceived:
                                _state = State.Data;
                                break;
                            case Action.InitializersExhausted:
                                _state = State.InitializersExhausted;
                                break;
                            case Action.SelectorReceived:
                                _state = State.Initialized;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(action), action, null);
                        }

                        break;
                    case State.Data:
                        switch (action)
                        {
                            case Action.DataReceived:
                                break;
                            case Action.InitializersExhausted:
                            case Action.SelectorReceived:
                                _state = State.Initialized;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(action), action, null);
                        }

                        break;
                    case State.InitializersExhausted:
                        switch (action)
                        {
                            case Action.InitializersExhausted:
                                break;
                            case Action.DataReceived:
                            case Action.SelectorReceived:
                                _state = State.Initialized;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(action), action, null);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (_state == State.Initialized) _taskCompletionSource.TrySetResult(true);
            }

            public void Apply(DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> changeSet, bool exhausted, DataSourceCategory category)
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
                if (category != DataSourceCategory.Initializers || newState != DataSourceState.Off) return;

                DetermineState(Action.InitializersExhausted);
            }

            public void Dispose()
            {
                _taskCompletionSource.TrySetResult(false);
            }
        }
    }
}
