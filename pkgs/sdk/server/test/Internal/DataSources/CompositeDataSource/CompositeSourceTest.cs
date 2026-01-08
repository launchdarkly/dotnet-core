using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using Xunit.Abstractions;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.AssertHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    public class CompositeSourceTest : BaseTest
    {
        public CompositeSourceTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        [Fact]
        public async Task CanFallbackOnInterrupted()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create action applier that responds to interrupted state
            IDataSourceObserver sharedActionApplier = null;
            ICompositeSourceActionable capturedActionable = null;

            ActionApplierFactory actionApplierFactory = (actionable) =>
            {
                capturedActionable = actionable;
                var applier = new MockActionApplier(actionable);
                sharedActionApplier = applier;
                return applier;
            };

            // First factory: creates a data source that reports initializing -> interrupted -> off
            SourceFactory firstSourceFactory = (updatesSink) =>
            {
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Interrupted, DataSourceState.Off }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Second factory: creates a data source that reports initializing -> interrupted -> off
            SourceFactory secondSourceFactory = (updatesSink) =>
            {
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Interrupted, DataSourceState.Off }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Third factory: creates a data source that reports initializing -> valid
            SourceFactory thirdSourceFactory = (updatesSink) =>
            {
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Valid }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Create CompositeSource with three factory tuples
            var factoryTuples = new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>
            {
                (firstSourceFactory, actionApplierFactory),
                (secondSourceFactory, actionApplierFactory),
                (thirdSourceFactory, actionApplierFactory)
            };

            var compositeSource = new CompositeSource(capturingSink, factoryTuples);

            // Start the composite source
            var startTask = compositeSource.Start();

            // Wait for the first data source to report initializing
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Verify we're at the first source initially
            Assert.True(compositeSource.IsAtFirst());

            // Wait for interrupted state
            WaitForStatus(capturingSink, DataSourceState.Interrupted);

            // Wait for valid state from the third source
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify that the composite source is initialized
            Assert.True(compositeSource.Initialized);

            // Verify we're no longer at the first source (we've moved to the third)
            Assert.False(compositeSource.IsAtFirst());

            compositeSource.Dispose();
        }

        [Fact]
        public async Task BlacklistsDataSourceFactoryAfterOffState()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Track how many times each factory is called
            int firstFactoryCallCount = 0;
            int secondFactoryCallCount = 0;

            // First factory: creates a data source that reports initializing -> interrupted -> off
            SourceFactory firstSourceFactory = (updatesSink) =>
            {
                firstFactoryCallCount++;
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Interrupted, DataSourceState.Off }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Create action applier that blacklists on Off and falls back to next factory
            ActionApplierFactory firstActionApplierFactory = (actionable) =>
            {
                return new MockActionApplierWithBlacklistOnOff(actionable);
            };

            // Second factory: creates a data source that reports initializing -> valid
            SourceFactory secondSourceFactory = (updatesSink) =>
            {
                secondFactoryCallCount++;
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Valid }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Second action applier factory (for the second data source)
            ActionApplierFactory secondActionApplierFactory = (actionable) =>
            {
                return new MockActionApplier(actionable);
            };

            // Create CompositeSource with two factory tuples
            var factoryTuples = new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>
            {
                (firstSourceFactory, firstActionApplierFactory),
                (secondSourceFactory, secondActionApplierFactory)
            };

            var compositeSource = new CompositeSource(capturingSink, factoryTuples, circular: true);

            // Start the composite source
            var startTask = compositeSource.Start();

            // Wait for the first data source to report initializing
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Wait for interrupted state (from first source reporting Interrupted)
            WaitForStatus(capturingSink, DataSourceState.Interrupted);

            // Note: When first source reports Off, the action applier sees the raw Off state
            // and triggers blacklisting, but DataSourceUpdatesSanitizer converts Off to Interrupted
            // for the sink. Since we already saw Interrupted, it won't be reported again.
            // The blacklisting and fallback to second source happens here.

            // Wait for valid state from the second source
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify that the composite source is initialized
            Assert.True(compositeSource.Initialized);

            // Verify that the first factory was called exactly once
            Assert.Equal(1, firstFactoryCallCount);

            // Verify that the second factory was called exactly once
            Assert.Equal(1, secondFactoryCallCount);

            // Now try to cycle through factories by calling GoToFirst to reset and cycle
            // Since the first factory is blacklisted, it should skip to the second factory
            compositeSource.DisposeCurrent();
            compositeSource.GoToFirst();
            await compositeSource.StartCurrent();

            // Wait a bit to ensure any factory calls complete
            await Task.Delay(100);

            // Verify that the first factory was still only called once (not called again after cycling)
            Assert.Equal(1, firstFactoryCallCount);

            // The second factory should be called again since it's the only remaining factory
            Assert.Equal(2, secondFactoryCallCount);

            // After GoToFirst, the second factory is now the first in the list (since first was blacklisted)
            // so IsAtFirst should be true
            Assert.True(compositeSource.IsAtFirst());

            compositeSource.Dispose();
        }

        [Fact]
        public async Task DisabledDataSourceCannotTriggerActions()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Track whether actions were triggered
            bool actionTriggered = false;
            int actionTriggerCount = 0;

            // Store the UpdateSink for the first data source so we can use it after it's disabled
            IDataSourceUpdatesV2 firstDataSourceUpdatesSink = null;

            // Create a mock data source that can be controlled to make updates after being replaced
            var firstDataSource = new MockMisbehavingDataSource(() =>
            {
                // This will be called after the first data source is replaced
                // Try to trigger an action by reporting Interrupted
                firstDataSourceUpdatesSink?.UpdateStatus(DataSourceState.Interrupted, null);
            });

            SourceFactory firstSourceFactory = (updatesSink) =>
            {
                firstDataSource.UpdateSink = updatesSink;
                return firstDataSource;
            };

            // Create an action applier that tracks when actions are triggered
            ActionApplierFactory firstActionApplierFactory = (actionable) =>
            {
                return new MockActionApplierWithTracking(actionable, () =>
                {
                    actionTriggered = true;
                    actionTriggerCount++;
                });
            };

            // Second factory: creates a data source that reports initializing -> valid
            SourceFactory secondSourceFactory = (updatesSink) =>
            {
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Valid }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Second action applier factory (for the second data source)
            ActionApplierFactory secondActionApplierFactory = (actionable) =>
            {
                return new MockActionApplier(actionable);
            };

            // Create CompositeSource with two factory tuples
            var factoryTuples = new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>
            {
                (firstSourceFactory, firstActionApplierFactory),
                (secondSourceFactory, secondActionApplierFactory)
            };

            var compositeSource = new CompositeSource(capturingSink, factoryTuples, circular: true);

            // Start the composite source
            var startTask = compositeSource.Start();

            // Wait for the first data source to report initializing
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Verify we're at the first source initially
            Assert.True(compositeSource.IsAtFirst());

            // Capture the UpdateSink that was passed to the first data source
            // This is the disableable wrapper that will be disabled when we move to the second source
            firstDataSourceUpdatesSink = firstDataSource.UpdateSink;

            // Now move to the second data source (this will call DisablePreviouslyTracked)
            compositeSource.DisposeCurrent();
            compositeSource.GoToNext();
            await compositeSource.StartCurrent();

            // Wait for valid state from the second source
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Verify we're no longer at the first source (we've moved to the second)
            Assert.False(compositeSource.IsAtFirst());

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            Assert.False(actionTriggered);
            Assert.Equal(0, actionTriggerCount);

            // Now have the first data source try to misbehave and make an update
            // This should be blocked because its updates sink has been disabled
            firstDataSource.TriggerUpdate();

            // Wait a bit to ensure any processing completes
            await Task.Delay(100);

            // Verify that no actions were triggered by the disabled data source
            Assert.False(actionTriggered);
            Assert.Equal(0, actionTriggerCount);

            // Verify that we didn't see any additional status updates from the misbehaving source
            // The Interrupted status from the disabled source should be blocked
            // Since WaitForStatus consumed the events we expected, we verify no unexpected events arrived
            capturingSink.StatusUpdates.ExpectNoValue(TimeSpan.FromMilliseconds(50));

            compositeSource.Dispose();
        }

        [Fact]
        public async Task DisposeReportsOffState()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create a simple data source factory that reports initializing -> valid
            SourceFactory sourceFactory = (updatesSink) =>
            {
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Valid }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Create a simple action applier factory
            ActionApplierFactory actionApplierFactory = (actionable) => { return new MockActionApplier(actionable); };

            // Create CompositeSource with one factory tuple
            var factoryTuples = new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>
            {
                (sourceFactory, actionApplierFactory)
            };

            var compositeSource = new CompositeSource(capturingSink, factoryTuples);

            // Start the composite source
            var startTask = compositeSource.Start();

            // Wait for the data source to report initializing
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Wait for valid state
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify that the composite source is initialized
            Assert.True(compositeSource.Initialized);

            // Verify we're at the first source (only one source in the list)
            Assert.True(compositeSource.IsAtFirst());

            // Dispose the composite source
            compositeSource.Dispose();

            // Verify that the final status update is Off (not Interrupted)
            // The sanitizer should not map Off to Interrupted when the composite itself is disposed
            WaitForStatus(capturingSink, DataSourceState.Off);
        }

        [Fact]
        public async Task AllThreeSourcesFailReportsOffWithExhaustedMessage()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create action applier factory that blacklists on Off and falls back to next factory
            ActionApplierFactory actionApplierFactory = (actionable) =>
            {
                return new MockActionApplierWithBlacklistOnOff(actionable);
            };

            // Create first source factory: reports initializing -> off
            SourceFactory firstSourceFactory = (updatesSink) =>
            {
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Off }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Create second source factory: reports initializing -> off
            SourceFactory secondSourceFactory = (updatesSink) =>
            {
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Off }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Create third source factory: reports initializing -> off
            SourceFactory thirdSourceFactory = (updatesSink) =>
            {
                var source = new MockDataSourceWithStatusSequence(
                    new[] { DataSourceState.Initializing, DataSourceState.Off }
                );
                source.UpdateSink = updatesSink;
                return source;
            };

            // Create CompositeSource with three factory tuples
            var factoryTuples = new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>
            {
                (firstSourceFactory, actionApplierFactory),
                (secondSourceFactory, actionApplierFactory),
                (thirdSourceFactory, actionApplierFactory)
            };

            var compositeSource = new CompositeSource(capturingSink, factoryTuples);

            // Start the composite source
            var startTask = compositeSource.Start();

            // Wait for Initializing status (from first source)
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Verify we're at the first source initially
            Assert.True(compositeSource.IsAtFirst());

            // Wait for Interrupted status (from first source failure)
            // Additional Interrupted statuses from subsequent source failures may be suppressed by the status sanitizer
            WaitForStatus(capturingSink, DataSourceState.Interrupted);

            // Wait for Off status (from composite source exhaustion) and verify the error message
            // After all three sources have failed, the composite should report Off with the exhaustion message
            var actualTimeout = TimeSpan.FromSeconds(5);
            ExpectPredicate(
                capturingSink.StatusUpdates,
                status => status.State == DataSourceState.Off &&
                          status.LastError.HasValue &&
                          status.LastError.Value.Message == "CompositeDataSource has exhausted all available sources.",
                "Did not receive Off status with expected error message within timeout",
                actualTimeout
            );

            // After moving to later sources, we should no longer be at first
            Assert.False(compositeSource.IsAtFirst());

            // Verify that the first Start() call completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify that a second call to Start() fails after all sources are exhausted
            var secondStartResult = await compositeSource.Start();
            Assert.False(secondStartResult);

            compositeSource.Dispose();
        }

        [Fact]
        public async Task NoSourcesProvidedReportsOffWithExhaustedMessage()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create CompositeSource with empty factory tuples list
            var factoryTuples = new List<(SourceFactory Factory, ActionApplierFactory ActionApplierFactory)>();

            var compositeSource = new CompositeSource(capturingSink, factoryTuples);

            // Start the composite source
            var startTask = compositeSource.Start();

            // Wait for Off status (from composite source exhaustion) and verify the error message
            // Since there are no sources, the composite should immediately report Off with the exhaustion message
            var actualTimeout = TimeSpan.FromSeconds(5);
            ExpectPredicate(
                capturingSink.StatusUpdates,
                status => status.State == DataSourceState.Off &&
                          status.LastError.HasValue &&
                          status.LastError.Value.Message == "CompositeDataSource has exhausted all available sources.",
                "Did not receive Off status with expected error message within timeout",
                actualTimeout
            );

            // Verify that Start() completed but returned false (no sources available)
            var startResult = await startTask;
            Assert.False(startResult, "Start() should return false when there are no sources");

            // Verify that IsAtFirst returns false when there are no sources (no current entry)
            Assert.False(compositeSource.IsAtFirst());

            // Verify that a second call to Start() also fails
            var secondStartResult = await compositeSource.Start();
            Assert.False(secondStartResult);

            compositeSource.Dispose();
        }

        private void WaitForStatus(CapturingDataSourceUpdatesWithHeaders sink, DataSourceState state,
            TimeSpan? timeout = null)
        {
            var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);
            ExpectPredicate(
                sink.StatusUpdates,
                status => status.State == state,
                $"Did not receive status {state} within timeout",
                actualTimeout
            );
        }

        // Mock implementations

        private class MockActionApplier : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;
            private readonly CapturingDataSourceUpdatesWithHeaders _capturingUpdates;

            public MockActionApplier(ICompositeSourceActionable actionable)
            {
                _actionable = actionable;
                _capturingUpdates = new CapturingDataSourceUpdatesWithHeaders();
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                // Capture the status update
                _capturingUpdates.UpdateStatus(newState, newError);

                // If we see interrupted state, trigger fallback
                if (newState == DataSourceState.Interrupted)
                {
                    _actionable.DisposeCurrent();
                    _actionable.GoToNext();
                    _actionable.StartCurrent();
                }
            }

            public void Dispose()
            {
                // Nothing to dispose
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                _capturingUpdates.Apply(changeSet);
            }

            public bool InitWithHeaders(FullDataSet<ItemDescriptor> allData,
                IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
            {
                return _capturingUpdates.InitWithHeaders(allData, headers);
            }
        }

        private class MockActionApplierWithBlacklistOnOff : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;
            private readonly CapturingDataSourceUpdatesWithHeaders _capturingUpdates;

            public MockActionApplierWithBlacklistOnOff(ICompositeSourceActionable actionable)
            {
                _actionable = actionable;
                _capturingUpdates = new CapturingDataSourceUpdatesWithHeaders();
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                // Capture the status update
                _capturingUpdates.UpdateStatus(newState, newError);

                // If we see Off state, blacklist the current source and fallback to next
                if (newState == DataSourceState.Off)
                {
                    _actionable.BlacklistCurrent();
                    _actionable.DisposeCurrent();
                    _actionable.GoToNext();
                    _actionable.StartCurrent();
                }
            }

            public void Dispose()
            {
                // Nothing to dispose
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                _capturingUpdates.Apply(changeSet);
            }
        }

        private class MockDataSourceWithStatusSequence : IDataSource
        {
            private readonly Queue<DataSourceState> _statusSequence;
            private readonly TaskCompletionSource<bool> _startCompletionSource;
            private bool _initialized;
            internal IDataSourceUpdatesV2 UpdateSink { get; set; }

            public MockDataSourceWithStatusSequence(DataSourceState[] statusSequence)
            {
                _statusSequence = new Queue<DataSourceState>(statusSequence);
                _startCompletionSource = new TaskCompletionSource<bool>();
            }

            public Task<bool> Start()
            {
                // Start() returns true immediately, status updates happen asynchronously
                _startCompletionSource.SetResult(true);

                // Report status updates asynchronously in the background
                _ = Task.Run(async () =>
                {
                    while (_statusSequence.Count > 0)
                    {
                        var state = _statusSequence.Dequeue();

                        // If we reached Valid, mark as initialized before reporting the status
                        if (state == DataSourceState.Valid)
                        {
                            _initialized = true;
                        }

                        UpdateSink?.UpdateStatus(state, null);

                        // Small delay to allow status updates to be processed
                        await Task.Delay(10);
                    }
                });

                return Task.FromResult(true);
            }

            public bool Initialized => _initialized;

            public void Dispose()
            {
                // Nothing to dispose
            }
        }

        private class MockActionApplierWithTracking : IDataSourceObserver
        {
            private readonly ICompositeSourceActionable _actionable;
            private readonly CapturingDataSourceUpdatesWithHeaders _capturingUpdates;
            private readonly Action _onActionTriggered;

            public MockActionApplierWithTracking(ICompositeSourceActionable actionable, Action onActionTriggered)
            {
                _actionable = actionable;
                _capturingUpdates = new CapturingDataSourceUpdatesWithHeaders();
                _onActionTriggered = onActionTriggered;
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                // Capture the status update
                _capturingUpdates.UpdateStatus(newState, newError);

                // If we see interrupted state, trigger fallback and notify
                if (newState == DataSourceState.Interrupted)
                {
                    _onActionTriggered?.Invoke();
                    _actionable.DisposeCurrent();
                    _actionable.GoToNext();
                    _actionable.StartCurrent();
                }
            }

            public void Dispose()
            {
                // Nothing to dispose
            }

            public void Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                _capturingUpdates.Apply(changeSet);
            }
        }

        private class MockMisbehavingDataSource : IDataSource
        {
            private readonly Action _triggerUpdate;
            internal IDataSourceUpdatesV2 UpdateSink { get; set; }

            public MockMisbehavingDataSource(Action triggerUpdate)
            {
                _triggerUpdate = triggerUpdate;
            }

            public Task<bool> Start()
            {
                // Report initializing immediately
                _ = Task.Run(async () => { UpdateSink?.UpdateStatus(DataSourceState.Initializing, null); });

                return Task.FromResult(true);
            }

            public bool Initialized => false;

            public void TriggerUpdate()
            {
                _triggerUpdate?.Invoke();
            }

            public void Dispose()
            {
                // Nothing to dispose
            }
        }
    }
}
