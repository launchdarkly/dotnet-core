using System;
using System.Collections.Immutable;
using LaunchDarkly.Cache;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    /// <summary>
    /// The SDK's internal implementation <see cref="IDataStore"/> for persistent data stores.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The basic data store behavior is provided by some implementation of
    /// <see cref="IPersistentDataStore"/> or <see cref="IPersistentDataStoreAsync"/>. This
    /// class adds the caching behavior that we normally want for any persistent data store.
    /// </para>
    /// </remarks>
    internal sealed class PersistentStoreWrapper : IDataStore, IExternalDataSourceSupport
    {
        private readonly IPersistentDataStore _core;
        private readonly DataStoreCacheConfig _caching;
        private readonly Logger _log;


        private PersistenceCacheContainer _caches;


        private readonly bool _cacheIndefinitely;
        private readonly PersistentDataStoreStatusManager _statusManager;

        private readonly object _externalStoreLock = new object();
        private IDataStoreExporter _externalDataStore;

        private volatile bool _inited;

        internal PersistentStoreWrapper(
            IPersistentDataStoreAsync coreAsync,
            DataStoreCacheConfig caching,
            IDataStoreUpdates dataStoreUpdates,
            TaskExecutor taskExecutor,
            Logger log,
            IDataStoreExporter externalDataStore = null
        ) :
            this(new PersistentStoreAsyncAdapter(coreAsync), caching, dataStoreUpdates, taskExecutor, log,
                externalDataStore)
        {
        }

        internal PersistentStoreWrapper(
            IPersistentDataStore core,
            DataStoreCacheConfig caching,
            IDataStoreUpdates dataStoreUpdates,
            TaskExecutor taskExecutor,
            Logger log,
            IDataStoreExporter externalDataStore = null
        )
        {
            _core = core;
            _caching = caching;
            _log = log;
            _externalDataStore = externalDataStore;

            _cacheIndefinitely = caching.IsEnabled && caching.IsInfiniteTtl;
            if (caching.IsEnabled)
            {
                var itemCacheBuilder = Caches.KeyValue<CacheKey, ItemDescriptor?>()
                    .WithLoader(GetInternalForCache)
                    .WithMaximumEntries(caching.MaximumEntries);
                var allCacheBuilder = Caches.KeyValue<DataKind, ImmutableDictionary<string, ItemDescriptor>>()
                    .WithLoader(GetAllAndDeserialize);
                var initCacheBuilder = Caches.SingleValue<bool>()
                    .WithLoader(_core.Initialized);
                if (!caching.IsInfiniteTtl)
                {
                    itemCacheBuilder.WithExpiration(caching.Ttl);
                    allCacheBuilder.WithExpiration(caching.Ttl);
                    initCacheBuilder.WithExpiration(caching.Ttl);
                }

                var itemCache = itemCacheBuilder.Build();
                var allCache = allCacheBuilder.Build();
                var initCache = initCacheBuilder.Build();
                _caches = new PersistenceCacheContainer(itemCache, allCache, initCache, caching.IsInfiniteTtl);
            }
            else
            {
                _caches = new PersistenceCacheContainer();
            }

            _statusManager = new PersistentDataStoreStatusManager(
                !_cacheIndefinitely,
                true,
                this.PollAvailabilityAfterOutage,
                dataStoreUpdates.UpdateStatus,
                taskExecutor,
                log
            );
        }

        public bool StatusMonitoringEnabled => true;

        public bool Initialized()
        {
            if (_inited)
            {
                return true;
            }

            var result = _caches.GetInit(() => _core.Initialized());

            if (result)
            {
                _inited = true;
            }

            return result;
        }

        /// <summary>
        /// Sets an external data source for recovery synchronization.
        /// </summary>
        /// <remarks>
        /// This should be called during initialization if the wrapper is being used
        /// in a write-through architecture where an external store maintains authoritative data.
        /// </remarks>
        /// <remarks>
        /// When we remove FDv1 support, we should remove this functionality and instead handle it at a higher
        /// layer.
        /// </remarks>
        /// <param name="externalDataSource">The external data source to sync from during recovery</param>
        public void SetExternalDataSource(IDataStoreExporter externalDataSource)
        {
            lock (_externalStoreLock)
            {
                _externalDataStore = externalDataSource;
            }
        }

        public void DisableCache()
        {
            _caches.Disable();
        }

        public void Init(FullDataSet<ItemDescriptor> items)
        {
            var serializedItems = items.Data.ToImmutableDictionary(
                kindAndItems => kindAndItems.Key,
                kindAndItems => PersistentDataStoreConverter.SerializeAll(kindAndItems.Key, kindAndItems.Value.Items)
            );
            Exception failure = InitCore(new FullDataSet<SerializedItemDescriptor>(serializedItems));
            _caches.SetFullDataSet(items, () =>
            {
                if (failure != null && !_caching.IsInfiniteTtl)
                {
                    // Normally, if the underlying store failed to do the update, we do not want to update the cache -
                    // the idea being that it's better to stay in a consistent state of having old data than to act
                    // like we have new data but then suddenly fall back to old data when the cache expires. However,
                    // if the cache TTL is infinite, then it makes sense to update the cache always.
                    throw failure;
                }
            });

            if (failure is null || _caching.IsInfiniteTtl)
            {
                _inited = true;
            }

            if (failure != null)
            {
                throw failure;
            }
        }

        public ItemDescriptor? Get(DataKind kind, string key)
        {
            try
            {
                var ret = _caches.GetItem(kind, key, () => GetAndDeserializeItem(kind, key));
                ProcessError(null);
                return ret;
            }
            catch (Exception e)
            {
                ProcessError(e);
                throw;
            }
        }

        public KeyedItems<ItemDescriptor> GetAll(DataKind kind)
        {
            try
            {
                var ret = new KeyedItems<ItemDescriptor>(_caches.GetAllForKind(kind, () => GetAllAndDeserialize(kind)));
                ProcessError(null);
                return ret;
            }
            catch (Exception e)
            {
                ProcessError(e);
                throw;
            }
        }

        public bool Upsert(DataKind kind, string key, ItemDescriptor item)
        {
            var serializedItem = PersistentDataStoreConverter.Serialize(kind, item);
            bool updated = false;
            Exception failure = null;
            try
            {
                updated = _core.Upsert(kind, key, serializedItem);
                ProcessError(null);
            }
            catch (Exception e)
            {
                // Normally, if the underlying store failed to do the update, we do not want to update the cache -
                // the idea being that it's better to stay in a consistent state of having old data than to act
                // like we have new data but then suddenly fall back to old data when the cache expires. However,
                // if the cache TTL is infinite, then it makes sense to update the cache always.
                ProcessError(e);
                if (!_caching.IsInfiniteTtl)
                {
                    throw;
                }

                failure = e;
            }

            _caches.SetItem(kind, key, item, failure != null, updated);

            if (failure != null)
            {
                throw failure;
            }

            return updated;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;
            _core.Dispose();
            _caches.Dispose();
            _statusManager.Dispose();
        }

        private ItemDescriptor? GetInternalForCache(CacheKey key) =>
            GetAndDeserializeItem(key.Kind, key.Key);

        private ItemDescriptor? GetAndDeserializeItem(DataKind kind, string key)
        {
            var maybeSerializedItem = _core.Get(kind, key);
            if (!maybeSerializedItem.HasValue)
            {
                return null;
            }

            return PersistentDataStoreConverter.Deserialize(kind, maybeSerializedItem.Value);
        }

        private ImmutableDictionary<string, ItemDescriptor> GetAllAndDeserialize(DataKind kind)
        {
            return _core.GetAll(kind).Items.ToImmutableDictionary(
                kv => kv.Key,
                kv => PersistentDataStoreConverter.Deserialize(kind, kv.Value));
        }

        private Exception InitCore(FullDataSet<SerializedItemDescriptor> allData)
        {
            try
            {
                _core.Init(allData);
                ProcessError(null);
                return null;
            }
            catch (Exception e)
            {
                ProcessError(e);
                return e;
            }
        }

        private void ProcessError(Exception e)
        {
            if (e == null)
            {
                // If we're waiting to recover after a failure, we'll let the polling routine take care
                // of signaling success. Even if we could signal success a little earlier based on the
                // success of whatever operation we just did, we'd rather avoid the overhead of acquiring
                // w.statusLock every time we do anything. So we'll just do nothing here.
                return;
            }

            _log.Error("Error from persistent data store: {0}", LogValues.ExceptionSummary(e));
            _statusManager.UpdateAvailability(false);
        }

        private bool PollAvailabilityAfterOutage()
        {
            if (!_core.IsStoreAvailable())
            {
                return false;
            }

            IDataStoreExporter externalDataStore;
            lock (_externalStoreLock)
            {
                externalDataStore = _externalDataStore;
            }

            // If we have an external data source (e.g., WriteThroughStore's memory store) that is initialized,
            // use that as the authoritative source. Otherwise, fall back to our internal cache if it's configured
            // to cache indefinitely.
            if (externalDataStore != null)
            {
                // Check if the external store has data (is initialized)
                // We use IDataStore interface to check initialization if available
                var externalStoreInitialized = false;
                if (externalDataStore is IDataStore externalStore)
                {
                    externalStoreInitialized = externalStore.Initialized();
                }

                if (externalStoreInitialized)
                {
                    try
                    {
                        var externalData = externalDataStore.ExportAllData();
                        var serializedData = PersistentDataStoreConverter.ToSerializedFormat(externalData);
                        var e = InitCore(serializedData);

                        if (e is null)
                        {
                            _log.Warn("Successfully updated persistent store from external data source");
                        }
                        else
                        {
                            // We failed to write the data to the underlying store. In this case, we should not
                            // return to a recovered state, but just try this all again next time the poll task runs.
                            LogHelpers.LogException(_log,
                                "Tried to write external data to persistent store after outage, but failed",
                                e);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        // If we can't export from the external source, don't recover yet
                        LogHelpers.LogException(_log,
                            "Failed to export data from external source during persistent store recovery",
                            ex);
                        return false;
                    }

                    return true;
                }
            }


            if (!_cacheIndefinitely) return true;

            // Fall back to cache-based recovery if external store is not available/initialized
            // and we're in infinite cache mode
            var all = _caches.GetAll();
            if (!all.HasValue) return true;
            var eInit = InitCore(all.Value);
            if (eInit is null)
            {
                _log.Warn("Successfully updated persistent store from cached data");
            }
            else
            {
                // We failed to write the cached data to the underlying store. In this case, we should not
                // return to a recovered state, but just try this all again next time the poll task runs.
                LogHelpers.LogException(_log,
                    "Tried to write cached data to persistent store after a store outage, but failed",
                    eInit);
                return false;
            }

            return true;
        }
    }

    internal struct CacheKey : IEquatable<CacheKey>
    {
        public readonly DataKind Kind;
        public readonly string Key;

        public CacheKey(DataKind kind, string key)
        {
            Kind = kind;
            Key = key;
        }

        public bool Equals(CacheKey other)
        {
            return Kind == other.Kind && Key == other.Key;
        }

        public override int GetHashCode()
        {
            return Kind.GetHashCode() * 17 + Key.GetHashCode();
        }
    }
}
