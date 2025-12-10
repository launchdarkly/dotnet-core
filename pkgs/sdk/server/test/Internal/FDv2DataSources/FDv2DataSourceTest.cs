using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
                        await Task.Delay(10);

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
                        await Task.Delay(10);

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

            // Wait for all expected status updates to be recorded
            // Expected sequence: Initializing (first initializer), Interrupted (first initializer failure),
            // Valid (second initializer), Interrupted (switching to synchronizer), Valid (synchronizer)
            var statusUpdates = capturingSink.WaitForStatusUpdates(5, TimeSpan.FromSeconds(5));

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify status updates by position
            // Position 0: Initializing (from first initializer)
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Initializing, statusUpdates[0].State);

            // Position 1: Interrupted (from first initializer failure)
            Assert.True(statusUpdates.Count > 1, "Expected at least 2 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[1].State);

            // The initializing status of the second initializer is not reported because it is suppressed by the status sanitizer

            // Position 2: Valid (from second initializer)
            Assert.True(statusUpdates.Count > 2, "Expected at least 3 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[2].State);

            // Position 3: Interrupted (from switching to synchronizer)
            Assert.True(statusUpdates.Count > 3, "Expected at least 4 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[3].State);

            // Position 4: Valid (from synchronizer)
            Assert.True(statusUpdates.Count > 4, "Expected at least 5 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[4].State);

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
                        await Task.Delay(10);

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
                        await Task.Delay(10);
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

            // Wait for all expected status updates to be recorded
            // Expected sequence: Initializing (first initializer), Valid (first initializer)
            var statusUpdates = capturingSink.WaitForStatusUpdates(2, TimeSpan.FromSeconds(5));

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify status updates by position
            // Position 0: Initializing (from first initializer)
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Initializing, statusUpdates[0].State);

            // Position 1: Valid (from first initializer)
            Assert.True(statusUpdates.Count > 1, "Expected at least 2 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[1].State);

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
                        await Task.Delay(10);

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

            // Wait for all expected status updates to be recorded
            // Expected sequence: 
            // 1. Initializing (from first initializer)
            // 2. Interrupted (from first initializer failure)
            // 3. Interrupted (from initializers exhausted error)
            // 4. Interrupted (from synchronizer's initializing state being mapped to interrupted)
            // 5. Valid (from synchronizer)
            var statusUpdates = capturingSink.WaitForStatusUpdates(5, TimeSpan.FromSeconds(5));

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify status updates by position
            // Position 0: Initializing (from first initializer)
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Initializing, statusUpdates[0].State);

            // Position 1: Interrupted (from first initializer failure)
            Assert.True(statusUpdates.Count > 1, "Expected at least 2 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[1].State);

            // Position 2: Interrupted (from with initializers exhausted error)
            Assert.True(statusUpdates.Count > 2, "Expected at least 3 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[2].State);
            // Check that the error at position 2 is the exhausted message
            Assert.True(statusUpdates[2].LastError.HasValue, "Expected error message at position 2");
            Assert.Contains("exhausted", statusUpdates[2].LastError.Value.Message, StringComparison.OrdinalIgnoreCase);

            // Position 3: Interrupted (from with synchronizer's initializing state being mapped to interrupted)
            Assert.True(statusUpdates.Count > 3, "Expected at least 4 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[3].State);

            // Position 4: Valid (from synchronizer)
            Assert.True(statusUpdates.Count > 4, "Expected at least 5 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[4].State);

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

        [Fact(Timeout = 10000)]
        public async Task AllThreeInitializersFailReportsOffWithExhaustedMessage()
        {
            TestLogger.Info("Test starting");

            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Track the update sinks for each initializer
            IDataSourceUpdates firstInitializerUpdateSink = null;
            IDataSourceUpdates secondInitializerUpdateSink = null;
            IDataSourceUpdates thirdInitializerUpdateSink = null;

            // Create first initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory firstInitializerFactory = (updatesSink) =>
            {
                TestLogger.Info("First initializer factory called");
                firstInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        TestLogger.Info("First initializer Start() executing");
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        TestLogger.Info("First initializer emitted Initializing");
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger fallback to second initializer
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        TestLogger.Info("First initializer emitted Off");
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create second initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory secondInitializerFactory = (updatesSink) =>
            {
                TestLogger.Info("Second initializer factory called");
                secondInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        TestLogger.Info("Second initializer Start() executing");
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        TestLogger.Info("Second initializer emitted Initializing");
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger fallback to third initializer
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        TestLogger.Info("Second initializer emitted Off");
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create third initializer factory: emits Initializing, then reports Off (failure)
            SourceFactory thirdInitializerFactory = (updatesSink) =>
            {
                TestLogger.Info("Third initializer factory called");
                thirdInitializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        TestLogger.Info("Third initializer Start() executing");
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        TestLogger.Info("Third initializer emitted Initializing");
                        await Task.Delay(10);

                        // Report Off (failure) - this should trigger exhaustion of all sources
                        updatesSink.UpdateStatus(DataSourceState.Off, null);
                        TestLogger.Info("Third initializer emitted Off");
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            TestLogger.Info("Creating FDv2DataSource");
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

            TestLogger.Info("Starting data source");
            // Start the data source
            var startTask = dataSource.Start();

            TestLogger.Info("Waiting for status updates (expecting 3)");
            // Wait for all expected status updates to be recorded
            // Expected sequence: Initializing (first initializer), Interrupted (first initializer failure),
            // Off (from composite source exhaustion)
            var statusUpdates = capturingSink.WaitForStatusUpdates(3, TimeSpan.FromSeconds(5));
            TestLogger.Info($"Received {statusUpdates.Count} status updates");

            TestLogger.Info("Awaiting start task");
            // Verify that the first Start() call completed successfully
            var startResult = await startTask;
            TestLogger.Info($"Start task completed with result: {startResult}");
            Assert.True(startResult);

            // Verify status updates by position
            // Position 0: Initializing (from first initializer)
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Initializing, statusUpdates[0].State);

            // Position 1: Interrupted (from first initializer failure)
            // Additional Interrupted statuses from subsequent initializer failures may be suppressed by the status sanitizer
            Assert.True(statusUpdates.Count > 1, "Expected at least 2 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[1].State);

            // Find the Off status with the exhaustion message
            var offStatus = statusUpdates.FirstOrDefault(s => s.State == DataSourceState.Off && 
                s.LastError.HasValue && 
                s.LastError.Value.Message == "CompositeDataSource has exhausted all available sources.");
            Assert.NotNull(offStatus);

            // Verify that a second call to Start() fails after all sources are exhausted
            var secondStartResult = await dataSource.Start();
            Assert.False(secondStartResult);

            dataSource.Dispose();
        }

        [Fact]
        public async Task NoInitializersOneSynchronizerIsWellBehaved()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Create dummy data for synchronizer
            var synchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track whether the synchronizer factory was invoked
            bool synchronizerFactoryInvoked = false;
            IDataSourceUpdates synchronizerUpdateSink = null;

            // Create synchronizer factory: emits Initializing, calls init with dummy data, then reports Valid
            SourceFactory synchronizerFactory = (updatesSink) =>
            {
                synchronizerFactoryInvoked = true;
                synchronizerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Valid
                        updatesSink.UpdateStatus(DataSourceState.Valid, null);
                        await Task.Delay(10);

                        // Call init with dummy data
                        updatesSink.Init(synchronizerDummyData);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with no initializers, synchronizer, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory>();
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

            // Wait for all expected status updates to be recorded
            // Expected sequence: Initializing (synchronizer), Valid (synchronizer)
            var statusUpdates = capturingSink.WaitForStatusUpdates(2, TimeSpan.FromSeconds(5));

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify status updates by position
            // Position 0: Initializing (from synchronizer)
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Initializing, statusUpdates[0].State);

            // Position 1: Valid (from synchronizer)
            Assert.True(statusUpdates.Count > 1, "Expected at least 2 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[1].State);

            // Verify that the synchronizer factory was invoked
            Assert.True(synchronizerFactoryInvoked, "Synchronizer factory should have been invoked");

            // Verify that init was called once with synchronizer dummy data
            var init = capturingSink.Inits.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(synchronizerDummyData, init);

            // Verify that there are no more init calls
            capturingSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task OneInitializerNoSynchronizerIsWellBehaved()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Create dummy data for initializer
            var initializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track whether the initializer factory was invoked
            bool initializerFactoryInvoked = false;
            IDataSourceUpdates initializerUpdateSink = null;

            // Create initializer factory: emits Initializing, calls init with dummy data, then reports Valid
            SourceFactory initializerFactory = (updatesSink) =>
            {
                initializerFactoryInvoked = true;
                initializerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Valid
                        updatesSink.UpdateStatus(DataSourceState.Valid, null);

                        // Call init with dummy data
                        updatesSink.Init(initializerDummyData);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with one initializer, no synchronizers, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory> { initializerFactory };
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

            // Wait for all expected status updates to be recorded
            // Expected sequence: Initializing (initializer), Valid (initializer)
            var statusUpdates = capturingSink.WaitForStatusUpdates(2, TimeSpan.FromSeconds(5));

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify status updates by position
            // Position 0: Initializing (from initializer)
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Initializing, statusUpdates[0].State);

            // Position 1: Valid (from initializer)
            Assert.True(statusUpdates.Count > 1, "Expected at least 2 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[1].State);

            // Verify that the initializer factory was invoked
            Assert.True(initializerFactoryInvoked, "Initializer factory should have been invoked");

            // Verify that init was called once with initializer dummy data
            var init = capturingSink.Inits.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(initializerDummyData, init);

            // Verify that there are no more init calls
            capturingSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task NoInitializersAndNoSynchronizersIsWellBehaved()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Create FDv2DataSource with no initializers, no synchronizers, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory>();
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

            // Wait for all expected status updates to be recorded
            // Expected: Off status (from composite source exhaustion)
            // Since there are no sources, the composite should report Off with the exhaustion message
            var statusUpdates = capturingSink.WaitForStatusUpdates(1, TimeSpan.FromSeconds(5));

            // Verify that Start() completed but returned false (no sources available)
            var startResult = await startTask;
            Assert.False(startResult, "Start() should return false when there are no sources");

            // Verify status updates by position
            // Position 0: Off status with exhaustion message
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Off, statusUpdates[0].State);
            Assert.True(statusUpdates[0].LastError.HasValue, "Expected error message in Off status");
            Assert.Equal("CompositeDataSource has exhausted all available sources.", statusUpdates[0].LastError.Value.Message);

            // Verify that init was never called
            capturingSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task CanDisposeWhenSynchronizersFallingBackUnthrottled()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdates();

            // Create error info to trigger immediate fallback
            var errorInfo = new DataSourceStatus.ErrorInfo
            {
                Kind = DataSourceStatus.ErrorKind.NetworkError,
                Time = DateTime.Now,
                Message = "Network error for testing"
            };

            // Track the update sink for the first synchronizer
            IDataSourceUpdates firstSynchronizerUpdateSink = null;

            // Create first synchronizer factory: immediately reports Off with error (triggers immediate fallback)
            SourceFactory firstSynchronizerFactory = (updatesSink) =>
            {
                firstSynchronizerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Immediately report Off with error - this triggers immediate fallback in unthrottled situation
                        updatesSink.UpdateStatus(DataSourceState.Off, errorInfo);
                    }
                );
                return source;
            };

            // Track the update sink for the second synchronizer
            IDataSourceUpdates secondSynchronizerUpdateSink = null;

            // Create second synchronizer factory: immediately reports Off with error (triggers immediate fallback)
            SourceFactory secondSynchronizerFactory = (updatesSink) =>
            {
                secondSynchronizerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Immediately report Off with error - this triggers immediate fallback in unthrottled situation
                        updatesSink.UpdateStatus(DataSourceState.Off, errorInfo);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with no initializers, two synchronizers, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory>();
            var synchronizers = new List<SourceFactory> { firstSynchronizerFactory, secondSynchronizerFactory };
            var fdv1Synchronizers = new List<SourceFactory>();

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                initializers,
                synchronizers,
                fdv1Synchronizers
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for all expected status updates to be recorded
            // Expected sequence: Off (first synchronizer), Off (second synchronizer after fallback)
            // There may also be Interrupted states during fallback
            var statusUpdates = capturingSink.WaitForStatusUpdates(2, TimeSpan.FromSeconds(5));

            // Verify status updates by position
            // Position 0: Interrupted (from first synchronizer)
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[0].State);

            // Position 1: Off (from second synchronizer after fallback)
            Assert.True(statusUpdates.Count > 1, "Expected at least 2 status updates");

            Assert.Equal(DataSourceState.Interrupted, statusUpdates[1].State);

            // Now dispose/stop the data source while synchronizers are falling back
            // This should complete without issues
            dataSource.Dispose();

            // Verify that Start() completed (it may have completed successfully or not, but should not hang)
            var startResult = await startTask;
            // The result may be true or false depending on implementation, but the key is that disposal works
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

