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
    public class FDv2DataSourceTest : BaseTest
    {
        public FDv2DataSourceTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        [Fact]
        public async Task FirstInitializerFailsSecondInitializerSucceedsSwitchesToSynchronizer()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Create dummy data for initializers and synchronizer
            var firstInitializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());
            var secondInitializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());
            var synchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track the update sink for the first initializer
            IDataSourceUpdates firstInitializerUpdateSink = null;

            // Create first initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory firstInitializerFactory = (updatesSink) =>
            {
                firstInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger fallback to second initializer
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Track the update sink for the second initializer so we can call init
            IDataSourceUpdates secondInitializerUpdateSink = null;

            // Create second initializer factory: emits Initializing, calls init with dummy data, then reports Valid
            SourceFactory secondInitializerFactory = (updatesSink) =>
            {
                secondInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Valid
                        updatesSink.UpdateStatus(DataSourceState.Valid, null);

                        // Call init with dummy data
                        updatesSink.Init(secondInitializerDummyData);
                        await Task.Delay(10);


                    }
                );
                return source;
            };

            // Track the update sink for the synchronizer so we can call init
            IDataSourceUpdates synchronizerUpdateSink = null;

            // Create synchronizer factory: emits Initializing, calls init with dummy data, then reports Valid
            SourceFactory synchronizerFactory = (updatesSink) =>
            {
                synchronizerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Valid
                        updatesSink.UpdateStatus(DataSourceState.Valid, null);

                        // Call init with dummy data
                        updatesSink.Init(synchronizerDummyData);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with two initializers, synchronizer, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory> { firstInitializerFactory, secondInitializerFactory };
            var synchronizers = new List<SourceFactory> { synchronizerFactory };
            var fdv1Synchronizers = new List<SourceFactory>();

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                initializers,
                synchronizers,
                fdv1Synchronizers
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for Initializing status (from first initializer)
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Wait for Interrupted status (from first initializer failure)
            WaitForStatus(capturingSink, DataSourceState.Interrupted);

            // The initializing status of the second initializer is not reported because it is suppressed by the status sanitizer

            // Wait for Valid status (from second initializer)
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Wait for Interrupted status (from switching to synchronizer)
            WaitForStatus(capturingSink, DataSourceState.Interrupted);

            // Wait for Valid status (from synchronizer)
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify that the data source is initialized
            // TODO: uncomment this check once Initialized is implemented
            // Assert.True(dataSource.Initialized);

            // Verify that init was called twice: once for second initializer, once for synchronizer
            // Verify the first init call was with second initializer dummy data
            var firstInit = capturingSink.Inits.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(secondInitializerDummyData, firstInit);

            // Verify the second init call was with synchronizer dummy data
            var secondInit = capturingSink.Inits.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(synchronizerDummyData, secondInit);

            dataSource.Dispose();
        }

        [Fact]
        public async Task FirstInitializerSucceedsSecondInitializerNotInvoked()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Create dummy data for initializers
            var firstInitializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());
            var secondInitializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track the update sink for the first initializer
            IDataSourceUpdates firstInitializerUpdateSink = null;

            // Create first initializer factory: emits Initializing, calls init with dummy data, then reports Valid
            SourceFactory firstInitializerFactory = (updatesSink) =>
            {
                firstInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Valid
                        updatesSink.UpdateStatus(DataSourceState.Valid, null);

                        // Call init with dummy data
                        updatesSink.Init(firstInitializerDummyData);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Track whether the second initializer factory was invoked
            bool secondInitializerFactoryInvoked = false;

            // Create second initializer factory: should not be invoked
            SourceFactory secondInitializerFactory = (updatesSink) =>
            {
                secondInitializerFactoryInvoked = true;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // This should never execute
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);
                        updatesSink.UpdateStatus(DataSourceState.Valid, null);
                        updatesSink.Init(secondInitializerDummyData);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with two initializers, empty synchronizers, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory> { firstInitializerFactory, secondInitializerFactory };
            var synchronizers = new List<SourceFactory>();
            var fdv1Synchronizers = new List<SourceFactory>();

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                initializers,
                synchronizers,
                fdv1Synchronizers
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for Initializing status (from first initializer)
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Wait for Valid status (from first initializer)
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify that the second initializer factory was never invoked
            Assert.False(secondInitializerFactoryInvoked, "Second initializer factory should not have been invoked");

            // Verify that init was called only once with first initializer dummy data
            var firstInit = capturingSink.Inits.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(firstInitializerDummyData, firstInit);

            // Verify that there are no more init calls
            capturingSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task AllInitializersFailSwitchesToSynchronizers()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Create dummy data for synchronizer
            var synchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track the update sink for the first initializer
            IDataSourceUpdates firstInitializerUpdateSink = null;

            // Create first initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory firstInitializerFactory = (updatesSink) =>
            {
                firstInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger fallback to second initializer
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Track the update sink for the second initializer
            IDataSourceUpdates secondInitializerUpdateSink = null;

            // Create second initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory secondInitializerFactory = (updatesSink) =>
            {
                secondInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger fallback to synchronizer
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Track the update sink for the synchronizer so we can call init
            IDataSourceUpdates synchronizerUpdateSink = null;

            // Create synchronizer factory: emits Initializing, calls init with dummy data, then reports Valid
            SourceFactory synchronizerFactory = (updatesSink) =>
            {
                synchronizerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Valid
                        updatesSink.UpdateStatus(DataSourceState.Valid, null);

                        // Call init with dummy data
                        updatesSink.Init(synchronizerDummyData);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with two initializers, synchronizer, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory> { firstInitializerFactory, secondInitializerFactory };
            var synchronizers = new List<SourceFactory> { synchronizerFactory };
            var fdv1Synchronizers = new List<SourceFactory>();

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                initializers,
                synchronizers,
                fdv1Synchronizers
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for Initializing status (from first initializer)
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Wait for Interrupted status (from first initializer failure)
            WaitForStatus(capturingSink, DataSourceState.Interrupted);

            // The initializing status of the second initializer is not reported because it is suppressed by the status sanitizer
            // The interrupted status of the second initializer is not reported because it is suppressed by the status sanitizer
            // The initializing status of the synchronizer is not reported because it is suppressed by the status sanitizer

            // Wait for Valid status (from synchronizer)
            WaitForStatus(capturingSink, DataSourceState.Valid);

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify that the data source is initialized
            // TODO: uncomment this check once Initialized is implemented
            // Assert.True(dataSource.Initialized);

            // Verify that init was called once with synchronizer dummy data
            var init = capturingSink.Inits.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(synchronizerDummyData, init);

            // Verify that there are no more init calls
            capturingSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task AllThreeInitializersFailReportsOffWithExhaustedMessage()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Track the update sinks for each initializer
            IDataSourceUpdates firstInitializerUpdateSink = null;
            IDataSourceUpdates secondInitializerUpdateSink = null;
            IDataSourceUpdates thirdInitializerUpdateSink = null;

            // Create first initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory firstInitializerFactory = (updatesSink) =>
            {
                firstInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger fallback to second initializer
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create second initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory secondInitializerFactory = (updatesSink) =>
            {
                secondInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger fallback to third initializer
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create third initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory thirdInitializerFactory = (updatesSink) =>
            {
                thirdInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger exhaustion of all sources
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with three initializers, empty synchronizers, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory> { firstInitializerFactory, secondInitializerFactory, thirdInitializerFactory };
            var synchronizers = new List<SourceFactory>();
            var fdv1Synchronizers = new List<SourceFactory>();

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                initializers,
                synchronizers,
                fdv1Synchronizers
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for Initializing status (from first initializer)
            WaitForStatus(capturingSink, DataSourceState.Initializing);

            // Wait for Interrupted status (from first initializer failure)
            // Additional Interrupted statuses from subsequent initializer failures may be suppressed by the status sanitizer
            WaitForStatus(capturingSink, DataSourceState.Interrupted);

            // Wait for Off status (from composite source exhaustion) and verify the error message
            // After all three initializers have failed, the composite should report Off with the exhaustion message
            var actualTimeout = TimeSpan.FromSeconds(5);
            ExpectPredicate(
                capturingSink.StatusUpdates,
                status => status.State == DataSourceState.Off && 
                         status.LastError.HasValue && 
                         status.LastError.Value.Message == "CompositeDataSource has exhausted all available sources.",
                "Did not receive Off status with expected error message within timeout",
                actualTimeout
            );

            // Verify that Start() completed
            var startResult = await startTask;
            Assert.False(startResult);

            dataSource.Dispose();
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

        // Mock data source that executes an async action when started
        private class MockDataSourceWithInit : IDataSource
        {
            private readonly Func<Task> _startAction;
            private bool _initialized;

            public MockDataSourceWithInit(Func<Task> startAction)
            {
                _startAction = startAction;
            }

            public async Task<bool> Start()
            {
                await _startAction();
                _initialized = true;
                return true;
            }

            public bool Initialized => _initialized;

            public void Dispose()
            {
                // Nothing to dispose
            }
        }
    }
}

