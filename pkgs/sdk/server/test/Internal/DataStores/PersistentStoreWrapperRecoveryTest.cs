using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Internal.DataStores.DataStoreTestTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    /// <summary>
    /// Tests for the external data source recovery behavior in PersistentStoreWrapper.
    /// These tests verify that when a persistent store recovers from an outage, it syncs
    /// data from an external data source (like InMemoryDataStore) rather than its internal cache.
    /// </summary>
    public class PersistentStoreWrapperRecoveryTest : BaseTest
    {
        private static readonly TimeSpan TimeoutForRecovery = TimeSpan.FromSeconds(2);
        private static readonly Exception FakeError = new NotImplementedException("test error");

        private readonly MockCoreSync _core;
        private readonly DataStoreUpdatesImpl _dataStoreUpdates;

        public PersistentStoreWrapperRecoveryTest(ITestOutputHelper testOutput) : base(testOutput)
        {
            _core = new MockCoreSync();
            _dataStoreUpdates = new DataStoreUpdatesImpl(BasicTaskExecutor, TestLogger);
        }

        private PersistentStoreWrapper MakeWrapperWithExternalSource(IDataStoreExporter externalSource)
        {
            return new PersistentStoreWrapper(
                _core,
                DataStoreCacheConfig.Enabled.WithTtl(System.Threading.Timeout.InfiniteTimeSpan),
                _dataStoreUpdates,
                BasicTaskExecutor,
                TestLogger,
                externalSource);
        }

        [Fact]
        public void ExternalDataSourceSync_WhenStoreRecovers_SyncsFromExternalSource()
        {
            // Create a mock external data source with some initial data
            var externalSource = new MockDataStoreExporter();
            var item1 = new TestItem("item1");
            var item2 = new TestItem("item2");

            externalSource.SetData(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(TestDataKind, "key2", 1, item2)
                .Build());

            using (var wrapper = MakeWrapperWithExternalSource(externalSource))
            {
                var dataStoreStatusProvider = new DataStoreStatusProviderImpl(wrapper, _dataStoreUpdates);
                var statuses = new EventSink<DataStoreStatus>();
                dataStoreStatusProvider.StatusChanged += statuses.Add;

                // Initialize the wrapper with some initial data
                wrapper.Init(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item1)
                    .Build());

                // Cause a store error
                _core.Available = false;
                _core.Error = FakeError;
                Assert.Equal(FakeError, Assert.Throws(FakeError.GetType(),
                    () => wrapper.Upsert(TestDataKind, "key1", item1.WithVersion(2))));

                var status1 = statuses.ExpectValue();
                Assert.False(status1.Available);

                // While the store is down, update the external data source with new data
                externalSource.SetData(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 2, item1)
                    .Add(TestDataKind, "key2", 2, item2)
                    .Add(TestDataKind, "key3", 1, new TestItem("item3"))
                    .Build());

                // Make store available again
                _core.Error = null;
                _core.Available = true;

                // Wait for recovery
                var status2 = statuses.ExpectValue(TimeoutForRecovery);
                Assert.True(status2.Available);
                Assert.False(status2.RefreshNeeded); // Should not need refresh in infinite cache mode

                // Verify that ALL data from external source was synced to persistent store
                Assert.Equal(item1.SerializedWithVersion(2), _core.Data[TestDataKind]["key1"]);
                Assert.Equal(item2.SerializedWithVersion(2), _core.Data[TestDataKind]["key2"]);
                Assert.Equal(new TestItem("item3").SerializedWithVersion(1), _core.Data[TestDataKind]["key3"]);

                AssertLogMessageRegex(true, LogLevel.Warn, "Successfully updated persistent store from external data source");
            }
        }

        [Fact]
        public void ExternalDataSourceSync_WithMultipleKinds_SyncsAllKinds()
        {
            var externalSource = new MockDataStoreExporter();
            var item1 = new TestItem("item1");
            var item2 = new TestItem("item2");

            externalSource.SetData(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(OtherDataKind, "key2", 1, item2)
                .Build());

            using (var wrapper = MakeWrapperWithExternalSource(externalSource))
            {
                var dataStoreStatusProvider = new DataStoreStatusProviderImpl(wrapper, _dataStoreUpdates);
                var statuses = new EventSink<DataStoreStatus>();
                dataStoreStatusProvider.StatusChanged += statuses.Add;

                wrapper.Init(externalSource.ExportAllData());

                // Cause error
                _core.Available = false;
                _core.Error = FakeError;
                Assert.Equal(FakeError, Assert.Throws(FakeError.GetType(),
                    () => wrapper.Upsert(TestDataKind, "key1", item1.WithVersion(2))));

                statuses.ExpectValue(); // consume unavailable status

                // Update both kinds in external source
                externalSource.SetData(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 3, item1)
                    .Add(OtherDataKind, "key2", 3, item2)
                    .Build());

                // Recover
                _core.Error = null;
                _core.Available = true;

                statuses.ExpectValue(TimeoutForRecovery); // wait for recovery

                // Both kinds should be synced
                Assert.Equal(item1.SerializedWithVersion(3), _core.Data[TestDataKind]["key1"]);
                Assert.Equal(item2.SerializedWithVersion(3), _core.Data[OtherDataKind]["key2"]);
            }
        }

        [Fact]
        public void ExternalDataSourceSync_WhenExportFails_DoesNotRecover()
        {
            var externalSource = new MockDataStoreExporter();
            externalSource.SetData(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, new TestItem("item1"))
                .Build());

            using (var wrapper = MakeWrapperWithExternalSource(externalSource))
            {
                var dataStoreStatusProvider = new DataStoreStatusProviderImpl(wrapper, _dataStoreUpdates);
                var statuses = new EventSink<DataStoreStatus>();
                dataStoreStatusProvider.StatusChanged += statuses.Add;

                wrapper.Init(externalSource.ExportAllData());

                // Cause error
                _core.Available = false;
                _core.Error = FakeError;
                Assert.Equal(FakeError, Assert.Throws(FakeError.GetType(),
                    () => wrapper.Upsert(TestDataKind, "key1", new TestItem("item1").WithVersion(2))));

                statuses.ExpectValue(); // consume unavailable status

                // Make external source throw an error during export
                var exportError = new InvalidOperationException("export failed");
                externalSource.ExportError = exportError;

                // Make store available, but external source will fail
                _core.Error = null;
                _core.Available = true;

                // Wait a bit to ensure polling happens (but not full recovery timeout)
                System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(600));

                // Should NOT have recovered because export failed
                // Try to get a status update with a short timeout - should fail
                try
                {
                    statuses.ExpectValue(TimeSpan.FromMilliseconds(100));
                    Assert.True(false, "Should not have received a status update");
                }
                catch (Xunit.Sdk.TrueException)
                {
                    // Expected - no status update received
                }

                AssertLogMessageRegex(true, LogLevel.Error, "Failed to export data from external source");
            }
        }

        [Fact]
        public void ExternalDataSourceSync_WhenInitCoreFails_DoesNotRecover()
        {
            var externalSource = new MockDataStoreExporter();
            externalSource.SetData(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, new TestItem("item1"))
                .Build());

            using (var wrapper = MakeWrapperWithExternalSource(externalSource))
            {
                var dataStoreStatusProvider = new DataStoreStatusProviderImpl(wrapper, _dataStoreUpdates);
                var statuses = new EventSink<DataStoreStatus>();
                dataStoreStatusProvider.StatusChanged += statuses.Add;

                wrapper.Init(externalSource.ExportAllData());

                // Cause error
                _core.Available = false;
                _core.Error = FakeError;
                Assert.Equal(FakeError, Assert.Throws(FakeError.GetType(),
                    () => wrapper.Upsert(TestDataKind, "key1", new TestItem("item1").WithVersion(2))));

                statuses.ExpectValue(); // consume unavailable status

                // Make store available but init will fail
                _core.Available = true;
                _core.Error = FakeError; // Still throws error on operations

                // Wait a bit for polling
                System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(600));

                // Should NOT have recovered because init failed
                // Try to get a status update with a short timeout - should fail
                try
                {
                    statuses.ExpectValue(TimeSpan.FromMilliseconds(100));
                    Assert.True(false, "Should not have received a status update");
                }
                catch (Xunit.Sdk.TrueException)
                {
                    // Expected - no status update received
                }

                AssertLogMessageRegex(true, LogLevel.Error, "Tried to write external data to persistent store after outage, but failed");
            }
        }

        [Fact]
        public void BackwardCompatibility_WithoutExternalSource_UsesCacheSync()
        {
            // This test verifies that when no external source is provided, the wrapper
            // falls back to the original cache-based recovery behavior
            using (var wrapper = new PersistentStoreWrapper(
                _core,
                DataStoreCacheConfig.Enabled.WithTtl(System.Threading.Timeout.InfiniteTimeSpan),
                _dataStoreUpdates,
                BasicTaskExecutor,
                TestLogger,
                externalDataStore: null)) // No external source
            {
                var dataStoreStatusProvider = new DataStoreStatusProviderImpl(wrapper, _dataStoreUpdates);
                var statuses = new EventSink<DataStoreStatus>();
                dataStoreStatusProvider.StatusChanged += statuses.Add;

                var item1 = new TestItem("item1");
                wrapper.Init(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item1)
                    .Build());

                // Cause error and update cache
                _core.Available = false;
                _core.Error = FakeError;
                Assert.Equal(FakeError, Assert.Throws(FakeError.GetType(),
                    () => wrapper.Upsert(TestDataKind, "key1", item1.WithVersion(2))));

                statuses.ExpectValue(); // consume unavailable status

                // The cache should have the update even though store failed
                Assert.Equal(item1.WithVersion(2), wrapper.Get(TestDataKind, "key1"));

                // Recover
                _core.Error = null;
                _core.Available = true;

                statuses.ExpectValue(TimeoutForRecovery);

                // Should have synced from cache (original behavior)
                Assert.Equal(item1.SerializedWithVersion(2), _core.Data[TestDataKind]["key1"]);

                AssertLogMessageRegex(true, LogLevel.Warn, "Successfully updated persistent store from cached data");
            }
        }

        [Fact]
        public void ExternalDataSourceSync_WithEmptyExternalSource_HandlesGracefully()
        {
            var externalSource = new MockDataStoreExporter();
            // External source has no data
            externalSource.SetData(new TestDataBuilder().Build());

            using (var wrapper = MakeWrapperWithExternalSource(externalSource))
            {
                var dataStoreStatusProvider = new DataStoreStatusProviderImpl(wrapper, _dataStoreUpdates);
                var statuses = new EventSink<DataStoreStatus>();
                dataStoreStatusProvider.StatusChanged += statuses.Add;

                wrapper.Init(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, new TestItem("item1"))
                    .Build());

                // Cause error
                _core.Available = false;
                _core.Error = FakeError;
                Assert.Equal(FakeError, Assert.Throws(FakeError.GetType(),
                    () => wrapper.Upsert(TestDataKind, "key1", new TestItem("item1").WithVersion(2))));

                statuses.ExpectValue();

                // Recover with empty external source
                _core.Error = null;
                _core.Available = true;

                statuses.ExpectValue(TimeoutForRecovery);

                // Should have cleared the persistent store (synced empty data)
                Assert.False(_core.Data.ContainsKey(TestDataKind) && _core.Data[TestDataKind].ContainsKey("key1"));
            }
        }

        [Fact]
        public void ExternalDataSourceSync_WithDeletedItems_SyncsCorrectly()
        {
            var externalSource = new MockDataStoreExporter();
            var item1 = new TestItem("item1");

            externalSource.SetData(new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, item1)
                .Add(TestDataKind, "key2", 1, null) // Deleted item
                .Build());

            using (var wrapper = MakeWrapperWithExternalSource(externalSource))
            {
                var dataStoreStatusProvider = new DataStoreStatusProviderImpl(wrapper, _dataStoreUpdates);
                var statuses = new EventSink<DataStoreStatus>();
                dataStoreStatusProvider.StatusChanged += statuses.Add;

                wrapper.Init(externalSource.ExportAllData());

                // Cause error
                _core.Available = false;
                _core.Error = FakeError;
                Assert.Equal(FakeError, Assert.Throws(FakeError.GetType(),
                    () => wrapper.Upsert(TestDataKind, "key1", item1.WithVersion(2))));

                statuses.ExpectValue();

                // Update external source with deleted item at higher version
                externalSource.SetData(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item1)
                    .Add(TestDataKind, "key2", 2, null) // Deleted item with higher version
                    .Build());

                // Recover
                _core.Error = null;
                _core.Available = true;

                statuses.ExpectValue(TimeoutForRecovery);

                // Verify deleted item was synced correctly
                Assert.True(_core.Data[TestDataKind].ContainsKey("key2"));
                var deletedItem = _core.Data[TestDataKind]["key2"];
                Assert.True(deletedItem.Deleted);
                Assert.Equal(2, deletedItem.Version);
            }
        }

        /// <summary>
        /// Mock implementation of IDataStoreExporter for testing.
        /// </summary>
        private class MockDataStoreExporter : IDataStoreExporter
        {
            private FullDataSet<ItemDescriptor> _data = new TestDataBuilder().Build();
            public Exception ExportError { get; set; }

            public void SetData(FullDataSet<ItemDescriptor> data)
            {
                _data = data;
            }

            public FullDataSet<ItemDescriptor> ExportAllData()
            {
                if (ExportError != null)
                {
                    throw ExportError;
                }
                return _data;
            }
        }
    }
}
