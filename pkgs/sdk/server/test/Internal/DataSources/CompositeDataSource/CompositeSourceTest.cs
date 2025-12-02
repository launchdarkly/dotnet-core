using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
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
            var capturingSink = new CapturingDataSourceUpdates();

            // Create action applier that responds to interrupted state
            IActionApplier sharedActionApplier = null;
            ICompositeSourceActionable capturedActionable = null;

            var actionApplierFactory = new MockActionApplierFactory((actionable) =>
            {
                capturedActionable = actionable;
                var applier = new MockActionApplier(actionable);
                sharedActionApplier = applier;
                return applier;
            });

            // First factory: creates a data source that reports initializing -> interrupted -> off
            var firstDataSource = new MockDataSourceWithStatusSequence(
                new[] { DataSourceState.Initializing, DataSourceState.Interrupted, DataSourceState.Off }
            );
            var firstSourceFactory = new MockSourceFactory(() => firstDataSource);

            // Second factory: creates a data source that reports initializing -> interrupted -> off
            var secondDataSource = new MockDataSourceWithStatusSequence(
                new[] { DataSourceState.Initializing, DataSourceState.Interrupted, DataSourceState.Off }
            );
            var secondSourceFactory = new MockSourceFactory(() => secondDataSource);

            // Third factory: creates a data source that reports initializing -> valid
            var thirdDataSource = new MockDataSourceWithStatusSequence(
                new[] { DataSourceState.Initializing, DataSourceState.Valid }
            );
            var thirdSourceFactory = new MockSourceFactory(() => thirdDataSource);

            // Create CompositeSource with three factory tuples
            var factoryTuples = new List<(ISourceFactory Factory, IActionApplierFactory ActionApplierFactory)>
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

            // Wait for interrupted state
            WaitForStatus(capturingSink, DataSourceState.Interrupted);

            // Wait for valid state from the third source
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify that the composite source is initialized
            Assert.True(compositeSource.Initialized);

            compositeSource.Dispose();
        }

        private void WaitForStatus(CapturingDataSourceUpdates sink, DataSourceState state, TimeSpan? timeout = null)
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

        private class MockSourceFactory : ISourceFactory
        {
            private readonly Func<IDataSource> _createFn;

            public MockSourceFactory(Func<IDataSource> createFn)
            {
                _createFn = createFn;
            }

            public IDataSource CreateSource(IDataSourceUpdates updatesSink)
            {
                var source = _createFn();
                if (source is MockDataSourceWithStatusSequence mockSource)
                {
                    mockSource.UpdateSink = updatesSink;
                }
                return source;
            }
        }

        private class MockActionApplierFactory : IActionApplierFactory
        {
            private readonly Func<ICompositeSourceActionable, IActionApplier> _createFn;

            public MockActionApplierFactory(Func<ICompositeSourceActionable, IActionApplier> createFn)
            {
                _createFn = createFn;
            }

            public IActionApplier CreateActionApplier(ICompositeSourceActionable actionable)
            {
                return _createFn(actionable);
            }
        }

        private class MockActionApplier : IActionApplier
        {
            private readonly ICompositeSourceActionable _actionable;
            private readonly CapturingDataSourceUpdates _capturingUpdates;

            public MockActionApplier(ICompositeSourceActionable actionable)
            {
                _actionable = actionable;
                _capturingUpdates = new CapturingDataSourceUpdates();
            }

            public IDataStoreStatusProvider DataStoreStatusProvider => _capturingUpdates.DataStoreStatusProvider;

            public bool Init(FullDataSet<ItemDescriptor> allData)
            {
                return _capturingUpdates.Init(allData);
            }

            public bool Upsert(DataKind kind, string key, ItemDescriptor item)
            {
                return _capturingUpdates.Upsert(kind, key, item);
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
        }

        private class MockDataSourceWithStatusSequence : IDataSource
        {
            private readonly Queue<DataSourceState> _statusSequence;
            private readonly TaskCompletionSource<bool> _startCompletionSource;
            private bool _initialized;
            internal IDataSourceUpdates UpdateSink { get; set; }

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
    }
}

