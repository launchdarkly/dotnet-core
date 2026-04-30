using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.FDv2DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    public class FDv2DataSourceTest : BaseTest
    {
        public FDv2DataSourceTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }
        
                [Fact]
        public async Task FirstInitializerFailsSecondInitializerSucceedsWithSelectorSwitchesToSynchronizer()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create dummy data for initializers and synchronizer
            var secondInitializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());
            var synchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track the update sink for the first initializer
            IDataSourceUpdatesV2 firstInitializerUpdateSink = null;

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
            IDataSourceUpdatesV2 secondInitializerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Make(1, "dummy-state"),
                            secondInitializerDummyData.Data,
                            null
                        ));
                        await Task.Delay(10);


                    }
                );
                return source;
            };

            // Track the update sink for the synchronizer so we can call init
            IDataSourceUpdatesV2 synchronizerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Make(2, "dummy-state"),
                            synchronizerDummyData.Data,
                            null
                        ));
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
                fdv1Synchronizers,
                TestLogger
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

            // Position 3: (Disposed Initializer)
            Assert.True(statusUpdates.Count > 3, "Expected at least 4 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[3].State);
            
            // Position 4: (Valid from synchronizer)
            Assert.True(statusUpdates.Count > 3, "Expected at least 4 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[4].State);

            // Verify that the data source is initialized
            Assert.True(dataSource.Initialized);

            // Verify that Apply was called twice: once for second initializer, once for synchronizer
            // Verify the first Apply call was with second initializer dummy data
            var firstChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, firstChangeSet.Type);

            // Verify the second Apply call was with synchronizer dummy data
            var secondChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, secondChangeSet.Type);

            dataSource.Dispose();
        }


        [Fact]
        public async Task FirstInitializerFailsSecondInitializerSucceedsWithoutSelectorSwitchesToSynchronizer()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create dummy data for initializers and synchronizer
            var secondInitializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());
            var synchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track the update sink for the first initializer
            IDataSourceUpdatesV2 firstInitializerUpdateSink = null;

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
            IDataSourceUpdatesV2 secondInitializerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Empty,
                            secondInitializerDummyData.Data,
                            null
                        ));
                        await Task.Delay(10);


                    }
                );
                return source;
            };

            // Track the update sink for the synchronizer so we can call init
            IDataSourceUpdatesV2 synchronizerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Make(2, "dummy-state"),
                            synchronizerDummyData.Data,
                            null
                        ));
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
                fdv1Synchronizers,
                TestLogger
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for all expected status updates to be recorded
            // Expected sequence: Initializing (first initializer), Interrupted (first initializer failure),
            // Valid (second initializer), Interrupted (switching to synchronizer), Valid (synchronizer)
            var statusUpdates = capturingSink.WaitForStatusUpdates(7, TimeSpan.FromSeconds(5));

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

            // Position 3: (Disposed init 1)
            Assert.True(statusUpdates.Count > 3, "Expected at least 4 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[3].State);
            
            // Position 4: (Exhausted initializers)
            Assert.True(statusUpdates.Count > 3, "Expected at least 4 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[4].State);
            
            // Position 5: (Disposed init 2) 
            Assert.True(statusUpdates.Count > 3, "Expected at least 4 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[5].State);

            // Position 6: Valid (Synchronizer)
            Assert.True(statusUpdates.Count > 4, "Expected at least 5 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[6].State);

            // Verify that the data source is initialized
            Assert.True(dataSource.Initialized);

            // Verify that Apply was called twice: once for second initializer, once for synchronizer
            // Verify the first Apply call was with second initializer dummy data
            var firstChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, firstChangeSet.Type);

            // Verify the second Apply call was with synchronizer dummy data
            var secondChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, secondChangeSet.Type);

            dataSource.Dispose();
        }

        [Fact]
        public async Task FirstInitializerSucceedsWithSelectorSecondInitializerNotInvoked()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create dummy data for initializers
            var firstInitializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());
            var secondInitializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track the update sink for the first initializer
            IDataSourceUpdatesV2 firstInitializerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Make(1, "dummy-state"),
                            firstInitializerDummyData.Data,
                            null
                        ));
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
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Empty,
                            secondInitializerDummyData.Data,
                            null
                        ));
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
                fdv1Synchronizers,
                TestLogger
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

            // Verify that Apply was called only once with first initializer dummy data
            var firstChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, firstChangeSet.Type);

            // Verify that there are no more Apply calls
            capturingSink.Applies.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task AllInitializersFailSwitchesToSynchronizers()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create dummy data for synchronizer
            var synchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track the update sink for the first initializer
            IDataSourceUpdatesV2 firstInitializerUpdateSink = null;

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
            IDataSourceUpdatesV2 secondInitializerUpdateSink = null;

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
            IDataSourceUpdatesV2 synchronizerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Empty,
                            synchronizerDummyData.Data,
                            null
                        ));
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
                fdv1Synchronizers,
                TestLogger
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
            Assert.True(dataSource.Initialized);

            // Verify that Apply was called once with synchronizer dummy data
            var changeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, changeSet.Type);

            // Verify that there are no more Apply calls
            capturingSink.Applies.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact(Timeout = 10000)]
        public async Task AllThreeInitializersFailReportsOffWithExhaustedMessage()
        {
            TestLogger.Info("Test starting");

            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Track the update sinks for each initializer
            IDataSourceUpdatesV2 firstInitializerUpdateSink = null;
            IDataSourceUpdatesV2 secondInitializerUpdateSink = null;
            IDataSourceUpdatesV2 thirdInitializerUpdateSink = null;

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
                fdv1Synchronizers,
                TestLogger
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
            Assert.False(startResult);

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
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create dummy data for synchronizer
            var synchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track whether the synchronizer factory was invoked
            bool synchronizerFactoryInvoked = false;
            IDataSourceUpdatesV2 synchronizerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Empty,
                            synchronizerDummyData.Data,
                            null
                        ));
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
                fdv1Synchronizers,
                TestLogger
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

            // Verify that Apply was called once with synchronizer dummy data
            var changeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, changeSet.Type);

            // Verify that there are no more Apply calls
            capturingSink.Applies.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task OneInitializerNoSynchronizerIsWellBehaved()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create dummy data for initializer
            var initializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track whether the initializer factory was invoked
            bool initializerFactoryInvoked = false;
            IDataSourceUpdatesV2 initializerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Empty,
                            initializerDummyData.Data,
                            null
                        ));
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
                fdv1Synchronizers,
                TestLogger
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

            // Verify that Apply was called once with initializer dummy data
            var changeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, changeSet.Type);

            // Verify that there are no more Apply calls
            capturingSink.Applies.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task OneInitializerOneSynchronizerIsWellBehaved()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create dummy data for initializer and synchronizer
            var initializerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());
            var synchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Track whether the initializer factory was invoked
            bool initializerFactoryInvoked = false;
            IDataSourceUpdatesV2 initializerUpdateSink = null;

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
                        await Task.Delay(10);

                        // Call Apply with dummy data (with selector to trigger switch to synchronizer)
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Make(1, "dummy-state"),
                            initializerDummyData.Data,
                            null
                        ));
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Track whether the synchronizer factory was invoked
            bool synchronizerFactoryInvoked = false;
            IDataSourceUpdatesV2 synchronizerUpdateSink = null;

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

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Make(2, "dummy-state"),
                            synchronizerDummyData.Data,
                            null
                        ));
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with one initializer, one synchronizer, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory> { initializerFactory };
            var synchronizers = new List<SourceFactory> { synchronizerFactory };
            var fdv1Synchronizers = new List<SourceFactory>();

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                initializers,
                synchronizers,
                fdv1Synchronizers,
                TestLogger
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for all expected status updates to be recorded
            // Expected sequence: Initializing (initializer), Valid (initializer), Interrupted (switching to synchronizer), Valid (synchronizer)
            var statusUpdates = capturingSink.WaitForStatusUpdates(4, TimeSpan.FromSeconds(5));

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

            // Position 2: Interrupted (switching to synchronizer)
            Assert.True(statusUpdates.Count > 2, "Expected at least 3 status updates");
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[2].State);

            // Position 3: Valid (from synchronizer)
            Assert.True(statusUpdates.Count > 3, "Expected at least 4 status updates");
            Assert.Equal(DataSourceState.Valid, statusUpdates[3].State);

            // Verify that both factories were invoked
            Assert.True(initializerFactoryInvoked, "Initializer factory should have been invoked");
            Assert.True(synchronizerFactoryInvoked, "Synchronizer factory should have been invoked");

            // Verify that Apply was called twice: once for initializer, once for synchronizer
            var firstChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, firstChangeSet.Type);

            var secondChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, secondChangeSet.Type);

            // Verify that there are no more Apply calls
            capturingSink.Applies.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task NoInitializersAndNoSynchronizersIsWellBehaved()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create FDv2DataSource with no initializers, no synchronizers, and empty fdv1Synchronizers
            var initializers = new List<SourceFactory>();
            var synchronizers = new List<SourceFactory>();
            var fdv1Synchronizers = new List<SourceFactory>();

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                initializers,
                synchronizers,
                fdv1Synchronizers,
                TestLogger
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for all expected status updates to be recorded
            // Expected: Off status (from composite source exhaustion)
            // Since there are no sources, the composite should report Off with the exhaustion message
            var statusUpdates = capturingSink.WaitForStatusUpdates(1, TimeSpan.FromSeconds(5));

            // Verify that Start() completed but returned true (no sources available)
            var startResult = await startTask;
            Assert.True(startResult, "Start() should return true when there are no sources");

            // Verify status updates by position
            // Position 0: Off status with exhaustion message
            Assert.True(statusUpdates.Count > 0, "Expected at least 1 status update");
            Assert.Equal(DataSourceState.Off, statusUpdates[0].State);
            Assert.True(statusUpdates[0].LastError.HasValue, "Expected error message in Off status");
            Assert.Equal("Composite source FDv2DataSource has exhausted its constituent sources.", statusUpdates[0].LastError.Value.Message);

            // Verify that Apply was never called
            capturingSink.Applies.ExpectNoValue(TimeSpan.FromMilliseconds(100));

            dataSource.Dispose();
        }

        [Fact]
        public async Task CanDisposeWhenSynchronizersFallingBackUnthrottled()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Create error info to trigger immediate fallback
            var errorInfo = new DataSourceStatus.ErrorInfo
            {
                Kind = DataSourceStatus.ErrorKind.NetworkError,
                Time = DateTime.Now,
                Message = "Network error for testing"
            };

            // Track the update sink for the first synchronizer
            IDataSourceUpdatesV2 firstSynchronizerUpdateSink = null;

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
            IDataSourceUpdatesV2 secondSynchronizerUpdateSink = null;

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
                fdv1Synchronizers,
                TestLogger
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

        [Fact]
        public async Task ErrorWithFDv1FallbackTriggersFallbackToFDv1Synchronizers()
        {
            // Create a capturing sink to observe all updates
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            // Track whether the synchronizer factory was invoked
            bool synchronizerFactoryInvoked = false;
            IDataSourceUpdatesV2 synchronizerUpdateSink = null;

            // Create synchronizer factory: emits Initializing, then reports Interrupted with FDv1Fallback error
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

                        // Report Interrupted with error that has FDv1Fallback = true
                        var errorInfo = new DataSourceStatus.ErrorInfo
                        {
                            Kind = DataSourceStatus.ErrorKind.ErrorResponse,
                            StatusCode = 503,
                            FDv1Fallback = true,
                            Time = DateTime.Now
                        };
                        updatesSink.UpdateStatus(DataSourceState.Interrupted, errorInfo);
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Track whether the fdv1Synchronizer factory was invoked
            bool fdv1SynchronizerFactoryInvoked = false;
            IDataSourceUpdatesV2 fdv1SynchronizerUpdateSink = null;

            // Create dummy data for fdv1Synchronizer
            var fdv1SynchronizerDummyData = new FullDataSet<ItemDescriptor>(new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Create fdv1Synchronizer factory: emits Initializing, calls init with dummy data, then reports Valid
            SourceFactory fdv1SynchronizerFactory = (updatesSink) =>
            {
                fdv1SynchronizerFactoryInvoked = true;
                fdv1SynchronizerUpdateSink = updatesSink;
                var source = new MockDataSourceWithInit(
                    async () =>
                    {
                        // Emit Initializing
                        updatesSink.UpdateStatus(DataSourceState.Initializing, null);
                        await Task.Delay(10);

                        // Report Valid
                        updatesSink.UpdateStatus(DataSourceState.Valid, null);
                        await Task.Delay(10);

                        // Call Apply with dummy data
                        updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                            ChangeSetType.Full,
                            Selector.Empty,
                            fdv1SynchronizerDummyData.Data,
                            null
                        ));
                        await Task.Delay(10);
                    }
                );
                return source;
            };

            // Create FDv2DataSource with no initializers, one synchronizer, and one fdv1Synchronizer
            var initializers = new List<SourceFactory>();
            var synchronizers = new List<SourceFactory> { synchronizerFactory };
            var fdv1Synchronizers = new List<SourceFactory> { fdv1SynchronizerFactory };

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                initializers,
                synchronizers,
                fdv1Synchronizers,
                TestLogger
            );

            // Start the data source
            var startTask = dataSource.Start();

            // Wait for status updates - we expect:
            // 1. Initializing (from synchronizer)
            // 2. Interrupted (from synchronizer with FDv1Fallback error)
            // 3. Interrupted (initializing from fdv1Synchronizer is mapped to interrupted by sanitizer after fallback)
            // 4. Valid (from fdv1Synchronizer)
            var statusUpdates = capturingSink.WaitForStatusUpdates(4, TimeSpan.FromSeconds(5));

            // Verify that Start() completed successfully
            var startResult = await startTask;
            Assert.True(startResult);

            // Verify status updates
            Assert.True(statusUpdates.Count >= 4, $"Expected at least 4 status updates, got {statusUpdates.Count}");

            // Position 0: Initializing (from synchronizer)
            Assert.Equal(DataSourceState.Initializing, statusUpdates[0].State);

            // Position 1: Interrupted (from synchronizer with FDv1Fallback error)
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[1].State);
            Assert.NotNull(statusUpdates[1].LastError);
            Assert.True(statusUpdates[1].LastError.Value.FDv1Fallback, "FDv1Fallback should be true in the error");

            // Position 2: Interrupted (initializing from fdv1Synchronizer is mapped to interrupted by sanitizer after fallback)
            Assert.Equal(DataSourceState.Interrupted, statusUpdates[2].State);

            // Position 3: Valid (from fdv1Synchronizer)
            Assert.Equal(DataSourceState.Valid, statusUpdates[3].State);

            // Verify that both factories were invoked
            Assert.True(synchronizerFactoryInvoked, "Synchronizer factory should have been invoked");
            Assert.True(fdv1SynchronizerFactoryInvoked, "FDv1Synchronizer factory should have been invoked after fallback");

            // Verify that Apply was called with fdv1Synchronizer dummy data
            var changeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
            Assert.Equal(ChangeSetType.Full, changeSet.Type);

            dataSource.Dispose();
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

        [Fact]
        public async Task FallbackAndRecoveryTasksWellBehaved()
        {
            // Use short timeouts for testing (50ms instead of 2 and 5 minutes)
            var fallbackTimeout = TimeSpan.FromMilliseconds(20);
            var recoveryTimeout = TimeSpan.FromMilliseconds(20);

            // Create a mock that tracks method calls
            var mockActionable = new MockCompositeSourceActionable();

            // Create the action applier with test-friendly timeouts
            var applier = new FDv2DataSource.ActionApplierTimedFallbackAndRecovery(
                mockActionable,
                TestLogger,
                fallbackTimeout,
                recoveryTimeout
            );

            // Test 1: Interrupted state should trigger fallback after timeout
            applier.UpdateStatus(DataSourceState.Interrupted, null);

            // Wait for the fallback task to complete
            await Task.Delay(fallbackTimeout + TimeSpan.FromMilliseconds(50));

            // Verify fallback was called: DisposeCurrent, GoToNext, StartCurrent
            Assert.True(mockActionable.DisposeCurrentCalled, "DisposeCurrent should be called on fallback");
            Assert.True(mockActionable.GoToNextCalled, "GoToNext should be called on fallback");
            Assert.True(mockActionable.StartCurrentCalled, "StartCurrent should be called on fallback");
            Assert.False(mockActionable.GoToFirstCalled, "GoToFirst should not be called on fallback");

            // Verify fallback log message was written
            AssertLogMessageRegex(true, LogLevel.Warn, ".*Current data source has been interrupted for more than.*falling back to next source.*");

            // Reset the mock for the next test
            mockActionable.Reset();

            // Test 2: Valid state should trigger recovery after timeout (when not at first)
            mockActionable.SetIsAtFirst(false);
            applier.UpdateStatus(DataSourceState.Valid, null);

            // Wait for the recovery task to complete
            await Task.Delay(recoveryTimeout + TimeSpan.FromMilliseconds(50));

            // Verify recovery was called: DisposeCurrent, GoToFirst, StartCurrent
            Assert.True(mockActionable.DisposeCurrentCalled, "DisposeCurrent should be called on recovery");
            Assert.True(mockActionable.GoToFirstCalled, "GoToFirst should be called on recovery");
            Assert.True(mockActionable.StartCurrentCalled, "StartCurrent should be called on recovery");
            Assert.False(mockActionable.GoToNextCalled, "GoToNext should not be called on recovery");

            // Verify recovery log message was written
            AssertLogMessageRegex(true, LogLevel.Info, ".*Current data source has been valid for more than.*recovering to primary source.*");

            // Reset the mock for the next test
            mockActionable.Reset();

            // Test 3: Valid state when IsAtFirst() returns true should NOT trigger recovery
            mockActionable.SetIsAtFirst(true);
            applier.UpdateStatus(DataSourceState.Valid, null);

            // Wait for the recovery timeout to pass
            await Task.Delay(recoveryTimeout + TimeSpan.FromMilliseconds(50));

            // Verify recovery was NOT called when already at first
            Assert.False(mockActionable.DisposeCurrentCalled, "DisposeCurrent should not be called when already at first");
            Assert.False(mockActionable.GoToFirstCalled, "GoToFirst should not be called when already at first");
            Assert.False(mockActionable.StartCurrentCalled, "StartCurrent should not be called when already at first");
            Assert.False(mockActionable.GoToNextCalled, "GoToNext should not be called when already at first");
        }

        private class MockCompositeSourceActionable : ICompositeSourceActionable
        {
            public bool DisposeCurrentCalled { get; private set; }
            public bool GoToNextCalled { get; private set; }
            public bool GoToFirstCalled { get; private set; }
            public bool StartCurrentCalled { get; private set; }
            public bool BlockCurrentCalled { get; private set; }
            public int GoToNextCallCount { get; private set; }
            public int BlockCurrentCallCount { get; private set; }
            public List<string> CallSequence { get; } = new List<string>();
            private bool _isAtFirst = false;

            public void DisposeCurrent()
            {
                DisposeCurrentCalled = true;
                CallSequence.Add(nameof(DisposeCurrent));
            }

            public void GoToNext()
            {
                GoToNextCalled = true;
                GoToNextCallCount++;
                CallSequence.Add(nameof(GoToNext));
            }

            public void GoToFirst()
            {
                GoToFirstCalled = true;
                CallSequence.Add(nameof(GoToFirst));
            }

            public Task<bool> StartCurrent()
            {
                StartCurrentCalled = true;
                CallSequence.Add(nameof(StartCurrent));
                return Task.FromResult(true);
            }

            public void BlockCurrent()
            {
                BlockCurrentCalled = true;
                BlockCurrentCallCount++;
                CallSequence.Add(nameof(BlockCurrent));
            }

            public bool IsAtFirst()
            {
                return _isAtFirst;
            }

            public void SetIsAtFirst(bool value)
            {
                _isAtFirst = value;
            }

            public void Reset()
            {
                DisposeCurrentCalled = false;
                GoToNextCalled = false;
                GoToFirstCalled = false;
                StartCurrentCalled = false;
                BlockCurrentCalled = false;
                GoToNextCallCount = 0;
                BlockCurrentCallCount = 0;
                CallSequence.Clear();
                _isAtFirst = false;
            }
        }

        [Fact]
        public void FDv1FallbackActionApplierIgnoresStatusWithoutFDv1Fallback()
        {
            var mockActionable = new MockCompositeSourceActionable();
            var applier = new FDv2DataSource.FDv1FallbackActionApplier(mockActionable);

            // Status updates without an FDv1Fallback error must not advance the composite.
            applier.UpdateStatus(DataSourceState.Off, null);
            applier.UpdateStatus(DataSourceState.Interrupted,
                new DataSourceStatus.ErrorInfo { Kind = DataSourceStatus.ErrorKind.NetworkError });
            applier.UpdateStatus(DataSourceState.Valid,
                new DataSourceStatus.ErrorInfo { FDv1Fallback = false });

            Assert.False(mockActionable.BlockCurrentCalled);
            Assert.False(mockActionable.GoToNextCalled);
            Assert.False(mockActionable.StartCurrentCalled);
        }

        [Fact]
        public void FDv1FallbackActionApplierWithDefaultSkipMovesToNextEntry()
        {
            // Default behavior (synchronizers entry): block current, dispose, go to next, start.
            var mockActionable = new MockCompositeSourceActionable();
            var applier = new FDv2DataSource.FDv1FallbackActionApplier(mockActionable);

            applier.UpdateStatus(
                DataSourceState.Off,
                new DataSourceStatus.ErrorInfo { FDv1Fallback = true });

            Assert.Equal(
                new List<string> { "BlockCurrent", "DisposeCurrent", "GoToNext", "StartCurrent" },
                mockActionable.CallSequence);
        }

        [Fact]
        public void FDv1FallbackActionApplierWithExtraSkipsAdvancesPastIntermediateEntries()
        {
            // From the initializers entry with synchronizers configured, the applier must skip
            // past the synchronizers entry to land on the FDv1 fallback entry.
            var mockActionable = new MockCompositeSourceActionable();
            var applier = new FDv2DataSource.FDv1FallbackActionApplier(mockActionable, extraEntriesToSkip: 1);

            applier.UpdateStatus(
                DataSourceState.Interrupted,
                new DataSourceStatus.ErrorInfo { FDv1Fallback = true });

            Assert.Equal(
                new List<string> { "BlockCurrent", "DisposeCurrent", "GoToNext", "BlockCurrent", "GoToNext", "StartCurrent" },
                mockActionable.CallSequence);
            Assert.Equal(2, mockActionable.GoToNextCallCount);
            Assert.Equal(2, mockActionable.BlockCurrentCallCount);
        }

        [Fact]
        public void GatedObserverSuppressesEventsAfterLatchTriggered()
        {
            // The latch coordinates the FDv1 fallback applier with the surrounding fallback /
            // recovery appliers: once one entry's applier has triggered the fallback, others must
            // not respond to the now-stale Off signals from disposed entries.
            var inner = new CapturingObserver();
            var latch = new FDv2DataSource.FDv1FallbackLatch();
            var gated = new FDv2DataSource.GatedObserver(inner, latch);

            // Before triggering: events flow through.
            gated.UpdateStatus(DataSourceState.Off, new DataSourceStatus.ErrorInfo());
            Assert.Equal(1, inner.UpdateStatusCallCount);

            // Trigger the latch (e.g. FDv1FallbackActionApplier observed the directive).
            Assert.True(latch.TryTrigger());

            // After triggering: events are suppressed.
            gated.UpdateStatus(DataSourceState.Off, new DataSourceStatus.ErrorInfo());
            gated.Apply(new ChangeSet<ItemDescriptor>(
                ChangeSetType.Full, Selector.Empty,
                new Dictionary<DataKind, KeyedItems<ItemDescriptor>>(), null));
            Assert.Equal(1, inner.UpdateStatusCallCount);
            Assert.Equal(0, inner.ApplyCallCount);
        }

        // End-to-end test: synchronizer reports Off+FDv1Fallback (mirrors the FDv2 streaming
        // source's Shutdown path on a 403 + x-ld-fd-fallback header). The SDK must engage the
        // FDv1 fallback synchronizer and reach Initialized via that path. The harness suite
        // "directive on streaming error engages FDv1 fallback" exercises this.
        [Fact]
        public async Task SynchronizerOffWithFDv1FallbackErrorEngagesFDv1FallbackSynchronizer()
        {
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();
            var fdv1Data = new FullDataSet<ItemDescriptor>(
                new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // Synchronizer reports Off (unrecoverable HTTP error) with the FDv1 fallback flag set
            // -- the same error the FDv2 streaming source emits for a 403+directive response.
            var synchronizerStartCount = 0;
            SourceFactory synchronizerFactory = (updatesSink) =>
            {
                synchronizerStartCount++;
                return new MockDataSourceWithInit(async () =>
                {
                    updatesSink.UpdateStatus(
                        DataSourceState.Off,
                        new DataSourceStatus.ErrorInfo
                        {
                            Kind = DataSourceStatus.ErrorKind.ErrorResponse,
                            StatusCode = 403,
                            FDv1Fallback = true,
                            Recoverable = false,
                            Time = DateTime.Now
                        });
                    await Task.Yield();
                });
            };

            // FDv1 fallback synchronizer applies a payload and reaches Valid -- the SDK must
            // reach Initialized via this path (not via the FDv2 sync, which failed).
            var fdv1Started = false;
            SourceFactory fdv1Factory = (updatesSink) =>
                new MockDataSourceWithInit(async () =>
                {
                    fdv1Started = true;
                    updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                        ChangeSetType.Full, Selector.Empty, fdv1Data.Data, null));
                    updatesSink.UpdateStatus(DataSourceState.Valid, null);
                    await Task.Yield();
                });

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                new List<SourceFactory>(),
                new List<SourceFactory> { synchronizerFactory },
                new List<SourceFactory> { fdv1Factory },
                TestLogger);

            try
            {
                var startResult = await dataSource.Start();
                Assert.True(startResult, "Start should complete via the FDv1 fallback path");
                Assert.True(fdv1Started, "FDv1 fallback synchronizer should have been started");

                // The Apply from FDv1 reached the data store -- this is the load-bearing
                // observation that proves the FDv1 fallback engaged.
                var changeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
                Assert.Equal(ChangeSetType.Full, changeSet.Type);
            }
            finally
            {
                dataSource.Dispose();
            }
        }

        // End-to-end test: synchronizer applies a payload first, then reports Off+FDv1Fallback
        // (mirrors the streaming success path with x-ld-fd-fallback header). The SDK must apply
        // the initial payload AND then engage FDv1 fallback. Harness suite "directive on
        // streaming success applies payload then engages FDv1" exercises this.
        [Fact]
        public async Task SynchronizerSuccessThenFDv1FallbackEngagesFDv1FallbackSynchronizer()
        {
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();
            var syncData = new FullDataSet<ItemDescriptor>(
                new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());
            var fdv1Data = new FullDataSet<ItemDescriptor>(
                new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            SourceFactory synchronizerFactory = (updatesSink) =>
                new MockDataSourceWithInit(async () =>
                {
                    // Apply a payload (the SDK's first observable side effect).
                    updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                        ChangeSetType.Full,
                        Selector.Make(1, "synchronizer-state"),
                        syncData.Data,
                        null));
                    // Then signal Off+FDv1Fallback. The action applier must engage FDv1 even
                    // though the data system is already Initialized via the payload above.
                    updatesSink.UpdateStatus(
                        DataSourceState.Off,
                        new DataSourceStatus.ErrorInfo
                        {
                            Kind = DataSourceStatus.ErrorKind.Unknown,
                            FDv1Fallback = true,
                            Time = DateTime.Now
                        });
                    await Task.Yield();
                });

            var fdv1Started = false;
            SourceFactory fdv1Factory = (updatesSink) =>
                new MockDataSourceWithInit(async () =>
                {
                    fdv1Started = true;
                    updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                        ChangeSetType.Full,
                        Selector.Make(2, "fdv1-state"),
                        fdv1Data.Data,
                        null));
                    await Task.Yield();
                });

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                new List<SourceFactory>(),
                new List<SourceFactory> { synchronizerFactory },
                new List<SourceFactory> { fdv1Factory },
                TestLogger);

            try
            {
                var startResult = await dataSource.Start();
                Assert.True(startResult);

                // Two Applies: first from the synchronizer's initial payload, then from FDv1.
                var firstChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
                Assert.Equal("synchronizer-state", firstChangeSet.Selector.State);
                var secondChangeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(2));
                Assert.Equal("fdv1-state", secondChangeSet.Selector.State);

                Assert.True(fdv1Started, "FDv1 fallback synchronizer should have been started");
            }
            finally
            {
                dataSource.Dispose();
            }
        }

        // End-to-end test: an initializer reports Off+FDv1Fallback. The SDK must skip the FDv2
        // synchronizer chain entirely and engage the FDv1 fallback synchronizer. Harness suite
        // "directive on polling initializer skips FDv2 synchronizers" exercises this.
        [Fact]
        public async Task InitializerOffWithFDv1FallbackErrorSkipsSynchronizersAndEngagesFDv1()
        {
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();
            var fdv1Data = new FullDataSet<ItemDescriptor>(
                new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            SourceFactory initializerFactory = (updatesSink) =>
                new MockDataSourceWithInit(async () =>
                {
                    updatesSink.UpdateStatus(
                        DataSourceState.Off,
                        new DataSourceStatus.ErrorInfo
                        {
                            Kind = DataSourceStatus.ErrorKind.ErrorResponse,
                            StatusCode = 403,
                            FDv1Fallback = true,
                            Recoverable = false,
                            Time = DateTime.Now
                        });
                    await Task.Yield();
                });

            // Synchronizer must NOT have its underlying data source's Start called. The composite
            // may call the factory while walking past the entry, but the action applier must
            // not start it.
            var synchronizerStarted = false;
            SourceFactory synchronizerFactory = (updatesSink) =>
                new MockDataSourceWithInit(async () =>
                {
                    synchronizerStarted = true;
                    updatesSink.UpdateStatus(DataSourceState.Valid, null);
                    await Task.Yield();
                });

            var fdv1Started = false;
            SourceFactory fdv1Factory = (updatesSink) =>
                new MockDataSourceWithInit(async () =>
                {
                    fdv1Started = true;
                    updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                        ChangeSetType.Full, Selector.Empty, fdv1Data.Data, null));
                    updatesSink.UpdateStatus(DataSourceState.Valid, null);
                    await Task.Yield();
                });

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                new List<SourceFactory> { initializerFactory },
                new List<SourceFactory> { synchronizerFactory },
                new List<SourceFactory> { fdv1Factory },
                TestLogger);

            try
            {
                var startResult = await dataSource.Start();
                Assert.True(startResult, "Start should complete via the FDv1 fallback path");

                var changeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(2));
                Assert.Equal(ChangeSetType.Full, changeSet.Type);

                Assert.True(fdv1Started, "FDv1 fallback synchronizer should have been started");
                Assert.False(synchronizerStarted,
                    "FDv2 synchronizer must not be started when the initializer signals FDv1 fallback");
            }
            finally
            {
                dataSource.Dispose();
            }
        }

        // End-to-end test: synchronizer reports the FDv1 fallback directive but no FDv1
        // fallback is configured. The SDK must HALT (Requirement 1.6.3(4)) -- which in
        // practice means the synchronizer's underlying data source must be disposed so it
        // stops trying to reconnect. The data system reaches Off via outer composite
        // exhaustion. Mirrors the harness suite "directive without FDv1 fallback configured
        // halts the data system".
        [Fact]
        public void SynchronizerFDv1FallbackWithoutFallbackConfiguredHaltsDataSystem()
        {
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();

            var synchronizerDisposed = false;
            SourceFactory synchronizerFactory = (updatesSink) =>
                new HaltMockDataSource(
                    onStart: async () =>
                    {
                        // 500 + directive: a recoverable status that would normally drive retries.
                        // The directive must take precedence and halt those retries.
                        updatesSink.UpdateStatus(
                            DataSourceState.Interrupted,
                            new DataSourceStatus.ErrorInfo
                            {
                                Kind = DataSourceStatus.ErrorKind.ErrorResponse,
                                StatusCode = 500,
                                FDv1Fallback = true,
                                Recoverable = true,
                                Time = DateTime.Now
                            });
                        await Task.Yield();
                    },
                    onDispose: () => synchronizerDisposed = true);

            // No FDv1 fallback configured (empty list).
            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                new List<SourceFactory>(),
                new List<SourceFactory> { synchronizerFactory },
                new List<SourceFactory>(),
                TestLogger);

            try
            {
                _ = dataSource.Start();

                // The data system must transition to Off via outer composite exhaustion. Wait a
                // few status updates -- intermediate Interrupted statuses come first.
                var statuses = capturingSink.WaitForStatusUpdates(2, TimeSpan.FromSeconds(5));
                Assert.Contains(statuses, s => s.State == DataSourceState.Off);

                // Critically, the synchronizer must have been disposed -- this is what stops the
                // streaming source from reconnecting in the harness scenario.
                Assert.True(synchronizerDisposed,
                    "synchronizer source must be disposed so the streaming source stops reconnecting");
            }
            finally
            {
                dataSource.Dispose();
            }
        }

        // Mock data source that reports its disposal -- used to verify that the FDv1 fallback
        // applier disposes the current synchronizer when no fallback is configured.
        private class HaltMockDataSource : IDataSource
        {
            private readonly Func<Task> _onStart;
            private readonly Action _onDispose;
            private bool _initialized;

            public HaltMockDataSource(Func<Task> onStart, Action onDispose)
            {
                _onStart = onStart;
                _onDispose = onDispose;
            }

            public async Task<bool> Start()
            {
                await _onStart();
                _initialized = true;
                return true;
            }

            public bool Initialized => _initialized;

            public void Dispose() => _onDispose();
        }

        // End-to-end test using the REAL FDv2StreamingDataSource (with a mock IEventSource so we
        // can drive synthetic SSE events). This catches harness-style regressions in the
        // streaming source's interaction with the FDv2DataSource composite that the simpler
        // mock-based tests above cannot reach. Mirrors the harness suite "directive on streaming
        // success applies payload then engages FDv1".
        [Fact]
        public async Task RealStreamingSourceWithFallbackHeaderEngagesFDv1FallbackAfterApply()
        {
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();
            var fdv1Data = new FullDataSet<ItemDescriptor>(
                new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            // The streaming factory is invoked asynchronously by the OUTER composite's queue
            // processor. Use a TCS to know when the streaming source has been constructed and
            // its event handlers wired to the mock event source -- otherwise the test thread can
            // race ahead and trigger the mock before the streaming source is listening.
            var streamingMock = new IntegrationMockEventSource();
            var streamingReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SourceFactory streamingFactory = (updatesSink) =>
            {
                var context = BasicContext.WithDataSourceUpdates(
                    new LaunchDarkly.Sdk.Server.Internal.DataSystem.DataSourceUpdatesV2ToV1Adapter(updatesSink));
                var source = new FDv2StreamingDataSource(
                    context,
                    context.DataSourceUpdates,
                    new Uri("http://example.com"),
                    TimeSpan.FromMilliseconds(10),
                    () => Selector.Empty,
                    (uri, http) => streamingMock);
                streamingReady.TrySetResult(true);
                return source;
            };

            var fdv1Started = false;
            SourceFactory fdv1Factory = (updatesSink) =>
                new MockDataSourceWithInit(async () =>
                {
                    fdv1Started = true;
                    updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                        ChangeSetType.Full,
                        Selector.Make(2, "fdv1-state"),
                        fdv1Data.Data,
                        null));
                    updatesSink.UpdateStatus(DataSourceState.Valid, null);
                    await Task.Yield();
                });

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                new List<SourceFactory>(),
                new List<SourceFactory> { streamingFactory },
                new List<SourceFactory> { fdv1Factory },
                TestLogger);

            try
            {
                var startTask = dataSource.Start();

                // Wait for the streaming factory to wire up before triggering events on the mock.
                Assert.True(await Task.WhenAny(streamingReady.Task, Task.Delay(TimeSpan.FromSeconds(2)))
                    == streamingReady.Task,
                    "streaming source should be constructed within 2s of dataSource.Start()");

                // Drive the mock event source with the headers + a complete xfer-full payload.
                var headers = new List<KeyValuePair<string, IEnumerable<string>>>
                {
                    new KeyValuePair<string, IEnumerable<string>>("x-ld-fd-fallback", new[] { "true" })
                };
                streamingMock.TriggerOpen(headers);
                streamingMock.TriggerMessage(new LaunchDarkly.EventSource.MessageReceivedEventArgs(
                    new LaunchDarkly.EventSource.MessageEvent("server-intent",
                        @"{""payloads"":[{""id"":""p1"",""target"":1,""intentCode"":""xfer-full"",""reason"":""r""}]}",
                        null)));
                streamingMock.TriggerMessage(new LaunchDarkly.EventSource.MessageReceivedEventArgs(
                    new LaunchDarkly.EventSource.MessageEvent("payload-transferred",
                        @"{""state"":""(p:p1:1)"",""version"":1}",
                        null)));

                var startResult = await startTask;
                Assert.True(startResult, "Start should complete via the streaming Apply");

                // Two Applies expected: streaming xfer-full (non-empty selector), then FDv1.
                var firstApply = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(1));
                Assert.Equal(ChangeSetType.Full, firstApply.Type);
                var secondApply = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(2));
                Assert.Equal("fdv1-state", secondApply.Selector.State);

                Assert.True(fdv1Started,
                    "FDv1 fallback synchronizer should be engaged when streaming success carries the FDv1 directive");
                Assert.True(streamingMock.IsClosed,
                    "Streaming event source should be closed after the directive is applied");
            }
            finally
            {
                dataSource.Dispose();
            }
        }

        // End-to-end test using the REAL FDv2StreamingDataSource: 403 error response with the
        // x-ld-fd-fallback header. Mirrors the harness suite "directive on streaming error
        // engages FDv1 fallback".
        [Fact]
        public async Task RealStreamingSource403WithFallbackHeaderEngagesFDv1Fallback()
        {
            var capturingSink = new CapturingDataSourceUpdatesWithHeaders();
            var fdv1Data = new FullDataSet<ItemDescriptor>(
                new Dictionary<DataKind, KeyedItems<ItemDescriptor>>());

            var streamingMock = new IntegrationMockEventSource();
            var streamingReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SourceFactory streamingFactory = (updatesSink) =>
            {
                var context = BasicContext.WithDataSourceUpdates(
                    new LaunchDarkly.Sdk.Server.Internal.DataSystem.DataSourceUpdatesV2ToV1Adapter(updatesSink));
                var source = new FDv2StreamingDataSource(
                    context,
                    context.DataSourceUpdates,
                    new Uri("http://example.com"),
                    TimeSpan.FromMilliseconds(10),
                    () => Selector.Empty,
                    (uri, http) => streamingMock);
                streamingReady.TrySetResult(true);
                return source;
            };

            var fdv1Started = false;
            SourceFactory fdv1Factory = (updatesSink) =>
                new MockDataSourceWithInit(async () =>
                {
                    fdv1Started = true;
                    updatesSink.Apply(new ChangeSet<ItemDescriptor>(
                        ChangeSetType.Full,
                        Selector.Empty,
                        fdv1Data.Data,
                        null));
                    updatesSink.UpdateStatus(DataSourceState.Valid, null);
                    await Task.Yield();
                });

            var dataSource = FDv2DataSource.CreateFDv2DataSource(
                capturingSink,
                new List<SourceFactory>(),
                new List<SourceFactory> { streamingFactory },
                new List<SourceFactory> { fdv1Factory },
                TestLogger);

            try
            {
                var startTask = dataSource.Start();

                Assert.True(await Task.WhenAny(streamingReady.Task, Task.Delay(TimeSpan.FromSeconds(2)))
                    == streamingReady.Task,
                    "streaming source should be constructed within 2s of dataSource.Start()");

                // The streaming source receives a 403 with the FDv1 fallback header. This is the
                // exact exception the EventSource library throws on a non-2xx response carrying
                // the directive (see EventSourceServiceUnsuccessfulResponseException in 5.3.0+).
                var headers = new List<KeyValuePair<string, IEnumerable<string>>>
                {
                    new KeyValuePair<string, IEnumerable<string>>("x-ld-fd-fallback", new[] { "true" })
                };
                var ex = new LaunchDarkly.EventSource.EventSourceServiceUnsuccessfulResponseException(
                    403, headers);
                streamingMock.TriggerError(ex);

                var startResult = await startTask;
                Assert.True(startResult,
                    "Start should complete: tracker reaches Initialized via FDv1 fallback Apply");

                var changeSet = capturingSink.Applies.ExpectValue(TimeSpan.FromSeconds(2));
                Assert.Equal(ChangeSetType.Full, changeSet.Type);

                Assert.True(fdv1Started,
                    "FDv1 fallback synchronizer should be engaged on a 403 + directive response");
            }
            finally
            {
                dataSource.Dispose();
            }
        }

        // Mock EventSource for integration tests -- duplicates the helper from
        // FDv2StreamingDataSourceTest to keep the integration test contained in this file.
        // Suppress unused-event warnings: we only use Opened/MessageReceived/Error here.
#pragma warning disable 67
        private class IntegrationMockEventSource : LaunchDarkly.EventSource.IEventSource
        {
            public event EventHandler<LaunchDarkly.EventSource.StateChangedEventArgs> Opened;
            public event EventHandler<LaunchDarkly.EventSource.StateChangedEventArgs> Closed;
            public event EventHandler<LaunchDarkly.EventSource.MessageReceivedEventArgs> MessageReceived;
            public event EventHandler<LaunchDarkly.EventSource.ExceptionEventArgs> Error;
            public event EventHandler<LaunchDarkly.EventSource.CommentReceivedEventArgs> CommentReceived;

            public bool IsClosed { get; private set; }
            public LaunchDarkly.EventSource.ReadyState ReadyState { get; private set; } =
                LaunchDarkly.EventSource.ReadyState.Closed;

            public Task StartAsync()
            {
                ReadyState = LaunchDarkly.EventSource.ReadyState.Open;
                return Task.CompletedTask;
            }

            public void Close() => IsClosed = true;
            public void Restart(bool forceNewConnection = false) { }

            public void TriggerOpen(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers = null) =>
                Opened?.Invoke(this, new LaunchDarkly.EventSource.StateChangedEventArgs(
                    LaunchDarkly.EventSource.ReadyState.Open, headers));

            public void TriggerMessage(LaunchDarkly.EventSource.MessageReceivedEventArgs args) =>
                MessageReceived?.Invoke(this, args);

            public void TriggerError(Exception exception) =>
                Error?.Invoke(this, new LaunchDarkly.EventSource.ExceptionEventArgs(exception));
        }
#pragma warning restore 67

        private class CapturingObserver : IDataSourceObserver
        {
            public int UpdateStatusCallCount { get; private set; }
            public int ApplyCallCount { get; private set; }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
                => UpdateStatusCallCount++;

            public void Apply(ChangeSet<ItemDescriptor> changeSet) => ApplyCallCount++;
        }
    }
}

