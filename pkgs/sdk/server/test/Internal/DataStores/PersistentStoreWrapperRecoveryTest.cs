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

        [Fact]
        public void ExternalDataSourceSync_WhenExternalStoreNotInitialized_FallsBackToCache()
        {
            // Create an uninitialized external store
            var externalSource = new MockDataStoreExporter();
            externalSource.IsInitialized = false; // Not initialized

            using (var wrapper = new PersistentStoreWrapper(
                _core,
                DataStoreCacheConfig.Enabled.WithTtl(System.Threading.Timeout.InfiniteTimeSpan),
                _dataStoreUpdates,
                BasicTaskExecutor,
                TestLogger,
                externalSource))
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

                // Update external source with different data, but keep it uninitialized
                externalSource.SetData(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 99, new TestItem("wrong-item"))
                    .Build());

                // Recover
                _core.Error = null;
                _core.Available = true;

                statuses.ExpectValue(TimeoutForRecovery);

                // Should have synced from CACHE (not external source) because external store is not initialized
                Assert.Equal(item1.SerializedWithVersion(2), _core.Data[TestDataKind]["key1"]);

                AssertLogMessageRegex(true, LogLevel.Warn, "Successfully updated persistent store from cached data");
            }
        }

        #region Cache Disabling Tests

        [Fact]
        public void DisableCache_PreventsSubsequentReads_FromUsingCache()
        {
            // Arrange
            using (var wrapper = MakeWrapperWithExternalSource(null))
            {
                var item1 = new TestItem("item1");
                var item2 = new TestItem("item2");

                // Initialize wrapper with item1
                wrapper.Init(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item1)
                    .Build());

                // Verify item1 is cached
                var cachedResult = wrapper.Get(TestDataKind, "key1");
                Assert.Equal(item1, cachedResult.Value.Item);

                // Change the underlying data in the core store
                _core.Data[TestDataKind]["key1"] = new SerializedItemDescriptor(2, false, TestItem.Serialize(new ItemDescriptor(2, item2)));

                // Before disable, should still get cached item1
                cachedResult = wrapper.Get(TestDataKind, "key1");
                Assert.Equal(item1, cachedResult.Value.Item);

                // Act - Disable cache
                wrapper.DisableCache();

                // Assert - Should now get item2 from core, not cached item1
                var directResult = wrapper.Get(TestDataKind, "key1");
                Assert.Equal(item2, directResult.Value.Item);
                Assert.Equal(2, directResult.Value.Version);
            }
        }

        [Fact]
        public void DisableCache_PreventsSubsequentWrites_FromPopulatingCache()
        {
            // Arrange
            using (var wrapper = MakeWrapperWithExternalSource(null))
            {
                var item1 = new TestItem("item1");
                var item2 = new TestItem("item2");

                // Initialize
                wrapper.Init(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item1)
                    .Build());

                // Act - Disable cache
                wrapper.DisableCache();

                // Perform an upsert
                wrapper.Upsert(TestDataKind, "key2", new ItemDescriptor(1, item2));

                // Make core unavailable to verify cache wasn't populated
                _core.Available = false;
                _core.Error = FakeError;

                // Assert - Should fail reading key2 because cache wasn't populated
                Assert.Throws<NotImplementedException>(() => wrapper.Get(TestDataKind, "key2"));
            }
        }

        [Fact]
        public void DisableCache_ClearsExistingCache()
        {
            // Arrange
            using (var wrapper = MakeWrapperWithExternalSource(null))
            {
                var item1 = new TestItem("item1");
                var item2 = new TestItem("item2");

                // Populate cache
                wrapper.Init(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item1)
                    .Build());

                // Verify cache is populated
                var cachedResult = wrapper.Get(TestDataKind, "key1");
                Assert.Equal(item1, cachedResult.Value.Item);

                // Change underlying data
                _core.Data[TestDataKind]["key1"] = new SerializedItemDescriptor(2, false, TestItem.Serialize(new ItemDescriptor(2, item2)));

                // Act - Disable cache
                wrapper.DisableCache();

                // Assert - Should get item2 from core (cache was cleared)
                var result = wrapper.Get(TestDataKind, "key1");
                Assert.Equal(item2, result.Value.Item);
                Assert.Equal(2, result.Value.Version);
            }
        }

        [Fact]
        public void DisableCache_DuringConcurrentReads_NoExceptions()
        {
            // Arrange
            using (var wrapper = MakeWrapperWithExternalSource(null))
            {
                var item = new TestItem("item1");
                wrapper.Init(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item)
                    .Build());

                var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
                var readCount = 0;
                var iterations = 1000;
                var readsStarted = new System.Threading.Tasks.TaskCompletionSource<bool>();

                // Act
                var readTasks = new List<System.Threading.Tasks.Task>();
                for (int t = 0; t < 5; t++)
                {
                    var taskIndex = t;
                    readTasks.Add(System.Threading.Tasks.Task.Run(() =>
                    {
                        for (int i = 0; i < iterations; i++)
                        {
                            if (taskIndex == 0 && i == 10)
                            {
                                readsStarted.TrySetResult(true);
                            }

                            try
                            {
                                wrapper.Get(TestDataKind, "key1");
                                System.Threading.Interlocked.Increment(ref readCount);
                            }
                            catch (Exception ex)
                            {
                                exceptions.Add(ex);
                            }
                        }
                    }));
                }

                // Wait for reads to start, then disable
                Assert.True(readsStarted.Task.Wait(TimeSpan.FromSeconds(2)), "Reads failed to start in time");
                wrapper.DisableCache();

                Assert.True(System.Threading.Tasks.Task.WaitAll(readTasks.ToArray(), TimeSpan.FromSeconds(10)),
                    "Read tasks did not complete in time");

                // Assert
                Assert.Empty(exceptions);
                Assert.Equal(5 * iterations, readCount);
            }
        }

        [Fact]
        public void DisableCache_DuringConcurrentWrites_NoExceptions()
        {
            // Arrange
            using (var wrapper = MakeWrapperWithExternalSource(null))
            {
                var item = new TestItem("item1");
                wrapper.Init(new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item)
                    .Build());

                var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
                var writeCount = 0;
                var iterations = 500;
                var writesStarted = new System.Threading.Tasks.TaskCompletionSource<bool>();

                // Act
                var writeTasks = new List<System.Threading.Tasks.Task>();
                for (int t = 0; t < 5; t++)
                {
                    var taskIndex = t;
                    writeTasks.Add(System.Threading.Tasks.Task.Run(() =>
                    {
                        for (int i = 0; i < iterations; i++)
                        {
                            if (taskIndex == 0 && i == 10)
                            {
                                writesStarted.TrySetResult(true);
                            }

                            try
                            {
                                wrapper.Upsert(TestDataKind, $"key{i % 10}", new ItemDescriptor(i, item));
                                System.Threading.Interlocked.Increment(ref writeCount);
                            }
                            catch (Exception ex)
                            {
                                exceptions.Add(ex);
                            }
                        }
                    }));
                }

                // Wait for writes to start, then disable
                Assert.True(writesStarted.Task.Wait(TimeSpan.FromSeconds(2)), "Writes failed to start in time");
                wrapper.DisableCache();

                Assert.True(System.Threading.Tasks.Task.WaitAll(writeTasks.ToArray(), TimeSpan.FromSeconds(10)),
                    "Write tasks did not complete in time");

                // Assert
                Assert.Empty(exceptions);
                Assert.Equal(5 * iterations, writeCount);
            }
        }

        [Fact]
        public void DisableCache_DuringInit_NoExceptions()
        {
            // Arrange
            using (var wrapper = MakeWrapperWithExternalSource(null))
            {
                var item = new TestItem("item1");
                var testData = new TestDataBuilder()
                    .Add(TestDataKind, "key1", 1, item)
                    .Build();

                var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
                var initCount = 0;
                var iterations = 100;
                var initsStarted = new System.Threading.Tasks.TaskCompletionSource<bool>();

                // Act
                var initTask = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        if (i == 5)
                        {
                            initsStarted.TrySetResult(true);
                        }

                        try
                        {
                            wrapper.Init(testData);
                            System.Threading.Interlocked.Increment(ref initCount);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });

                // Wait for inits to start, then disable
                Assert.True(initsStarted.Task.Wait(TimeSpan.FromSeconds(2)), "Inits failed to start in time");
                wrapper.DisableCache();

                Assert.True(initTask.Wait(TimeSpan.FromSeconds(10)), "Init task did not complete in time");

                // Assert
                Assert.Empty(exceptions);
                Assert.Equal(iterations, initCount);
            }
        }

        #endregion

        /// <summary>
        /// Mock implementation of IDataStoreExporter for testing.
        /// </summary>
        private class MockDataStoreExporter : IDataStoreExporter, IDataStore
        {
            private FullDataSet<ItemDescriptor> _data = new TestDataBuilder().Build();
            public Exception ExportError { get; set; }
            public bool IsInitialized { get; set; } = true;

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

            // IDataStore implementation
            public bool StatusMonitoringEnabled => false;

            public void Init(FullDataSet<ItemDescriptor> allData)
            {
                _data = allData;
                IsInitialized = true;
            }

            public ItemDescriptor? Get(DataKind kind, string key) => null;

            public KeyedItems<ItemDescriptor> GetAll(DataKind kind) => KeyedItems<ItemDescriptor>.Empty();

            public bool Upsert(DataKind kind, string key, ItemDescriptor item) => false;

            public bool Initialized() => IsInitialized;

            public void Dispose() { }
        }
    }
}
