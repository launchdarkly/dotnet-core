using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Cache;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Internal.DataStores.DataStoreTestTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    /// <summary>
    /// Tests for PersistenceCacheContainer to ensure cache disabling functionality
    /// works correctly and is thread-safe.
    /// </summary>
    public class PersistenceCacheContainerTest
    {
        private readonly TestItem _item1 = new TestItem("item1");
        private readonly TestItem _item2 = new TestItem("item2");

        private PersistenceCacheContainer CreateContainerWithCaches(bool isInfiniteTtl = true)
        {
            var itemCache = Caches.KeyValue<CacheKey, ItemDescriptor?>()
                .WithLoader(_ => null)
                .Build();
            var allCache = Caches.KeyValue<DataKind, ImmutableDictionary<string, ItemDescriptor>>()
                .WithLoader(_ => ImmutableDictionary<string, ItemDescriptor>.Empty)
                .Build();
            var initCache = Caches.SingleValue<bool>()
                .WithLoader(() => false)
                .Build();

            return new PersistenceCacheContainer(itemCache, allCache, initCache, isInfiniteTtl);
        }

        #region Basic Functionality Tests

        [Fact]
        public void Constructor_WithCaches_EnablesCaching()
        {
            // Arrange
            var container = CreateContainerWithCaches();

            // Act
            // Populate cache using SetItem (simulating a write operation)
            container.SetItem(TestDataKind, "key1", new ItemDescriptor(1, _item1), false, true);

            // Read from cache - the direct function should not be called since cache is enabled
            var directCalled = false;
            var result = container.GetItem(TestDataKind, "key1", () =>
            {
                directCalled = true;
                return new ItemDescriptor(999, _item2);
            });

            // Assert
            Assert.False(directCalled);
            Assert.True(result.HasValue);
            Assert.Equal(_item1, result.Value.Item);
            Assert.Equal(1, result.Value.Version);

            container.Dispose();
        }

        [Fact]
        public void Constructor_NoCaches_DisablesCaching()
        {
            // Arrange
            var container = new PersistenceCacheContainer();
            var directCallCount = 0;

            // Act
            container.GetItem(TestDataKind, "key1", () =>
            {
                directCallCount++;
                return new ItemDescriptor(1, _item1);
            });

            container.GetItem(TestDataKind, "key1", () =>
            {
                directCallCount++;
                return new ItemDescriptor(1, _item1);
            });

            // Assert - direct should be called each time
            Assert.Equal(2, directCallCount);

            container.Dispose();
        }

        [Fact]
        public void Disable_PreventsSubsequentCacheUse()
        {
            // Arrange
            var container = CreateContainerWithCaches();

            // Populate cache
            container.GetItem(TestDataKind, "key1", () => new ItemDescriptor(1, _item1));

            // Act
            container.Disable();

            // Now reads should bypass cache
            var directCalled = false;
            var result = container.GetItem(TestDataKind, "key1", () =>
            {
                directCalled = true;
                return new ItemDescriptor(2, _item2);
            });

            // Assert
            Assert.True(directCalled);
            Assert.Equal(_item2, result.Value.Item);
            Assert.Equal(2, result.Value.Version);

            container.Dispose();
        }

        [Fact]
        public void Disable_PreventsSetItemCacheUse()
        {
            // Arrange
            var container = CreateContainerWithCaches();

            // Act
            container.Disable();

            // Perform write operations
            container.SetItem(TestDataKind, "key1", new ItemDescriptor(1, _item1), false, true);

            // Try to read - should use direct function since cache disabled
            var directCalled = false;
            var result = container.GetItem(TestDataKind, "key1", () =>
            {
                directCalled = true;
                return new ItemDescriptor(2, _item2);
            });

            // Assert - direct was called, showing write didn't populate cache
            Assert.True(directCalled);
            Assert.Equal(_item2, result.Value.Item);

            container.Dispose();
        }

        [Fact]
        public void Disable_PreventsSetFullDataSetCacheUse()
        {
            // Arrange
            var container = CreateContainerWithCaches();

            // Act
            container.Disable();

            // Perform Init operation
            var testData = new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, _item1)
                .Build();

            container.SetFullDataSet(testData, () => { });

            // Try to read - should use direct function since cache disabled
            var directCalled = false;
            var result = container.GetItem(TestDataKind, "key1", () =>
            {
                directCalled = true;
                return new ItemDescriptor(2, _item2);
            });

            // Assert - direct was called, showing write didn't populate cache
            Assert.True(directCalled);
            Assert.Equal(_item2, result.Value.Item);

            container.Dispose();
        }

        [Fact]
        public void Disable_WithNoCaches_DoesNotThrow()
        {
            // Arrange
            var container = new PersistenceCacheContainer();

            // Act & Assert - should not throw
            container.Disable();

            container.Dispose();
        }

        [Fact]
        public void Disable_CalledMultipleTimes_IsIdempotent()
        {
            // Arrange
            var container = CreateContainerWithCaches();

            // Act & Assert - should not throw
            container.Disable();
            container.Disable();
            container.Disable();

            container.Dispose();
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public void GetInit_DuringDisable_NoExceptions()
        {
            // Arrange
            var container = CreateContainerWithCaches();
            var exceptions = new List<Exception>();
            var iterations = 1000;
            var readsStarted = new TaskCompletionSource<bool>();
            var disableAfterReads = 100; // Disable after this many reads

            // Act
            var readTask = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (i == 10) // Signal after a few reads have started
                    {
                        readsStarted.TrySetResult(true);
                    }

                    try
                    {
                        container.GetInit(() => true);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            });

            // Wait for reads to start, then disable
            Assert.True(readsStarted.Task.Wait(TimeSpan.FromSeconds(2)), "Reads failed to start in time");
            container.Disable();

            Assert.True(readTask.Wait(TimeSpan.FromSeconds(5)), "Read task did not complete in time");

            // Assert
            Assert.Empty(exceptions);

            container.Dispose();
        }

        [Fact]
        public void GetItem_DuringDisable_NoExceptions()
        {
            // Arrange
            var container = CreateContainerWithCaches();
            var exceptions = new List<Exception>();
            var iterations = 1000;
            var readsStarted = new TaskCompletionSource<bool>();

            // Act
            var readTask = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (i == 10)
                    {
                        readsStarted.TrySetResult(true);
                    }

                    try
                    {
                        container.GetItem(TestDataKind, "key1", () => new ItemDescriptor(1, _item1));
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            });

            Assert.True(readsStarted.Task.Wait(TimeSpan.FromSeconds(2)), "Reads failed to start in time");
            container.Disable();

            Assert.True(readTask.Wait(TimeSpan.FromSeconds(5)), "Read task did not complete in time");

            // Assert
            Assert.Empty(exceptions);

            container.Dispose();
        }

        [Fact]
        public void SetItem_DuringDisable_NoExceptions()
        {
            // Arrange
            var container = CreateContainerWithCaches();
            var exceptions = new List<Exception>();
            var iterations = 1000;
            var writesStarted = new TaskCompletionSource<bool>();

            // Act
            var writeTask = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (i == 10)
                    {
                        writesStarted.TrySetResult(true);
                    }

                    try
                    {
                        container.SetItem(TestDataKind, $"key{i}", new ItemDescriptor(1, _item1), false, true);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            });

            Assert.True(writesStarted.Task.Wait(TimeSpan.FromSeconds(2)), "Writes failed to start in time");
            container.Disable();

            Assert.True(writeTask.Wait(TimeSpan.FromSeconds(5)), "Write task did not complete in time");

            // Assert
            Assert.Empty(exceptions);

            container.Dispose();
        }

        [Fact]
        public void SetFullDataSet_DuringDisable_NoExceptions()
        {
            // Arrange
            var container = CreateContainerWithCaches();
            var exceptions = new List<Exception>();
            var iterations = 100; // Fewer iterations since this is expensive
            var writesStarted = new TaskCompletionSource<bool>();

            var testData = new TestDataBuilder()
                .Add(TestDataKind, "key1", 1, _item1)
                .Build();

            // Act
            var writeTask = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (i == 5)
                    {
                        writesStarted.TrySetResult(true);
                    }

                    try
                    {
                        container.SetFullDataSet(testData, () => { });
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            });

            Assert.True(writesStarted.Task.Wait(TimeSpan.FromSeconds(2)), "Writes failed to start in time");
            container.Disable();

            Assert.True(writeTask.Wait(TimeSpan.FromSeconds(5)), "Write task did not complete in time");

            // Assert
            Assert.Empty(exceptions);

            container.Dispose();
        }

        [Fact]
        public void ConcurrentDisable_Calls_NoExceptions()
        {
            // Arrange
            var container = CreateContainerWithCaches();
            var exceptions = new List<Exception>();
            var iterations = 100;

            // Act
            var tasks = new List<Task>();
            for (int t = 0; t < 10; t++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        try
                        {
                            container.Disable();
                        }
                        catch (Exception ex)
                        {
                            lock (exceptions)
                            {
                                exceptions.Add(ex);
                            }
                        }
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));

            // Assert
            Assert.Empty(exceptions);

            container.Dispose();
        }

        #endregion

        #region Race Condition Tests

        [Fact]
        public void Disable_WhileReading_ReadsCompleteCorrectly()
        {
            // Arrange
            var container = CreateContainerWithCaches();
            var exceptions = new List<Exception>();
            var results = new List<ItemDescriptor?>();
            var iterations = 1000;
            var disableAtIteration = 100;
            var readsBeforeDisable = new TaskCompletionSource<bool>();
            var disableComplete = new TaskCompletionSource<bool>();

            // Populate cache with known data using SetItem
            container.SetItem(TestDataKind, "key1", new ItemDescriptor(1, _item1), false, true);

            var readTask = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (i == disableAtIteration - 10)
                    {
                        readsBeforeDisable.TrySetResult(true);
                        // Wait for disable to complete before continuing
                        Assert.True(disableComplete.Task.Wait(TimeSpan.FromSeconds(2)), "Disable did not complete in time");
                    }

                    try
                    {
                        var result = container.GetItem(TestDataKind, "key1", () => new ItemDescriptor(2, _item2));
                        lock (results)
                        {
                            results.Add(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            });

            // Wait for reads to start, then disable
            Assert.True(readsBeforeDisable.Task.Wait(TimeSpan.FromSeconds(2)), "Reads before disable did not start in time");
            container.Disable();
            disableComplete.TrySetResult(true);

            Assert.True(readTask.Wait(TimeSpan.FromSeconds(5)), "Read task did not complete in time");

            // Assert
            Assert.Empty(exceptions);

            // All reads should return valid data (either cached _item1 or direct _item2)
            foreach (var result in results)
            {
                Assert.True(result.HasValue);
                Assert.True(result.Value.Item.Equals(_item1) || result.Value.Item.Equals(_item2));
            }

            // Some reads should have used direct function (after disable)
            var directUsed = results.Exists(r => r.HasValue && r.Value.Item.Equals(_item2));
            Assert.True(directUsed, "Expected some reads to use direct function after disable");

            container.Dispose();
        }

        [Fact]
        public void Disable_WhileWriting_WritesCompleteCorrectly()
        {
            // Arrange
            var container = CreateContainerWithCaches();
            var exceptions = new List<Exception>();
            var iterations = 1000;
            var disableAtIteration = 100;
            var writesBeforeDisable = new TaskCompletionSource<bool>();
            var disableComplete = new TaskCompletionSource<bool>();

            var writeTask = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (i == disableAtIteration - 10)
                    {
                        writesBeforeDisable.TrySetResult(true);
                        // Wait for disable to complete before continuing
                        Assert.True(disableComplete.Task.Wait(TimeSpan.FromSeconds(2)), "Disable did not complete in time");
                    }

                    try
                    {
                        container.SetItem(TestDataKind, $"key{i % 10}", new ItemDescriptor(i, _item1), false, true);
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            });

            // Wait for writes to start, then disable
            Assert.True(writesBeforeDisable.Task.Wait(TimeSpan.FromSeconds(2)), "Writes before disable did not start in time");
            container.Disable();
            disableComplete.TrySetResult(true);

            Assert.True(writeTask.Wait(TimeSpan.FromSeconds(5)), "Write task did not complete in time");

            // Assert
            Assert.Empty(exceptions);

            // Verify cache is disabled - reads should use direct function
            var directCalled = false;
            container.GetItem(TestDataKind, "key1", () =>
            {
                directCalled = true;
                return new ItemDescriptor(999, _item2);
            });

            Assert.True(directCalled, "Cache should be disabled after Disable() called");

            container.Dispose();
        }

        #endregion
    }
}
