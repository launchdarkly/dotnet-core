using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Cache;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    internal class PersistenceCacheContainer : IDisposable
    {
        private readonly ICache<CacheKey, DataStoreTypes.ItemDescriptor?> _itemCache;

        private readonly ICache<DataStoreTypes.DataKind, ImmutableDictionary<string, DataStoreTypes.ItemDescriptor>>
            _allCache;

        private readonly ISingleValueCache<bool> _initCache;
        private bool _cachingEnabled;
        private readonly bool _isInfiniteTtl;
        private readonly List<DataStoreTypes.DataKind> _cachedDataKinds = new List<DataStoreTypes.DataKind>();

        private readonly object _cacheLock = new object();

        public PersistenceCacheContainer()
        {
            _cachingEnabled = false;
        }

        public PersistenceCacheContainer(ICache<CacheKey, DataStoreTypes.ItemDescriptor?> itemCache,
            ICache<DataStoreTypes.DataKind, ImmutableDictionary<string, DataStoreTypes.ItemDescriptor>> allCache,
            ISingleValueCache<bool> initCache, bool isInfiniteTtl)
        {
            _itemCache = itemCache;
            _allCache = allCache;
            _initCache = initCache;
            _cachingEnabled = true;
            _isInfiniteTtl = isInfiniteTtl;
        }

        public bool GetInit(Func<bool> direct)
        {
            lock (_cacheLock)
            {
                if (_cachingEnabled)
                {
                    return _initCache.Get();
                }
            }

            return direct();
        }

        public void SetFullDataSet(DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> items, Action preCondition)
        {
            lock (_cacheLock)
            {
                if (!_cachingEnabled) return;

                _allCache.Clear();
                _itemCache.Clear();

                _cachedDataKinds.Clear();
                foreach (var kv in items.Data)
                {
                    _cachedDataKinds.Add(kv.Key);
                }


                preCondition();

                foreach (var e0 in items.Data)
                {
                    var kind = e0.Key;
                    _allCache.Set(kind, e0.Value.Items.ToImmutableDictionary());
                    foreach (var e1 in e0.Value.Items)
                    {
                        _itemCache.Set(new CacheKey(kind, e1.Key), e1.Value);
                    }
                }
            }
        }

        public ImmutableDictionary<string, DataStoreTypes.ItemDescriptor> GetAllForKind(DataStoreTypes.DataKind kind,
            Func<ImmutableDictionary<string, DataStoreTypes.ItemDescriptor>> direct)
        {
            lock (_cacheLock)
            {
                if (_cachingEnabled)
                {
                    return _allCache.Get(kind);
                }
            }

            return direct();
        }

        public DataStoreTypes.FullDataSet<DataStoreTypes.SerializedItemDescriptor>?
            GetAll()
        {
            lock (_cacheLock)
            {
                if (!_cachingEnabled) return null;
                // If we're in infinite cache mode, then we can assume the cache has a full set of current
                // flag data (since presumably the data source has still been running) and we can just
                // write the contents of the cache to the underlying data store.
                DataStoreTypes.DataKind[] allKinds;

                allKinds = _cachedDataKinds.ToArray();


                var builder =
                    ImmutableList
                        .CreateBuilder<KeyValuePair<DataStoreTypes.DataKind,
                            DataStoreTypes.KeyedItems<DataStoreTypes.SerializedItemDescriptor>>>();
                foreach (var kind in allKinds)
                {
                    if (_allCache.TryGetValue(kind, out var items))
                    {
                        builder.Add(
                            new KeyValuePair<DataStoreTypes.DataKind,
                                DataStoreTypes.KeyedItems<DataStoreTypes.SerializedItemDescriptor>>(kind,
                                PersistentDataStoreConverter.SerializeAll(kind, items)));
                    }
                }

                return new DataStoreTypes.FullDataSet<DataStoreTypes.SerializedItemDescriptor>(builder.ToImmutable());
            }
        }

        public void SetItem(DataStoreTypes.DataKind kind, string key, DataStoreTypes.ItemDescriptor item,
            bool failedPersistence,
            bool updatedPersistence)
        {
            lock (_cacheLock)
            {
                if (!_cachingEnabled) return;

                var cacheKey = new CacheKey(kind, key);
                if (!failedPersistence)
                {
                    if (updatedPersistence)
                    {
                        _itemCache.Set(cacheKey, item);
                    }
                    else
                    {
                        // there was a concurrent modification elsewhere - update the cache to get the new state
                        _itemCache.Remove(cacheKey);
                        _itemCache.Get(cacheKey);
                    }
                }
                else
                {
                    try
                    {
                        var oldItem = _itemCache.Get(cacheKey);
                        if (!oldItem.HasValue || oldItem.Value.Version < item.Version)
                        {
                            _itemCache.Set(cacheKey, item);
                        }
                    }
                    catch (Exception)
                    {
                        // An exception here means that the underlying database is down *and* there was no
                        // cached item; in that case we just go ahead and update the cache.
                        _itemCache.Set(cacheKey, item);
                    }
                }

                // If the cache has a finite TTL, then we should remove the "all items" cache entry to force
                // a reread the next time All is called. However, if it's an infinite TTL, we need to just
                // update the item within the existing "all items" entry (since we want things to still work
                // even if the underlying store is unavailable).
                if (_isInfiniteTtl)
                {
                    try
                    {
                        var cachedAll = _allCache.Get(kind);
                        _allCache.Set(kind, cachedAll.SetItem(key, item));
                    }
                    catch (Exception)
                    {
                    }
                    // An exception here means that we did not have a cached value for All, so it tried to query
                    // the underlying store, which failed (not surprisingly since it just failed a moment ago
                    // when we tried to do an update). This should not happen in infinite-cache mode, but if it
                    // does happen, there isn't really anything we can do.
                }
                else
                {
                    _allCache.Remove(kind);
                }
            }
        }

        public DataStoreTypes.ItemDescriptor? GetItem(DataStoreTypes.DataKind kind, string key,
            Func<DataStoreTypes.ItemDescriptor?> direct)
        {
            lock (_cacheLock)
            {
                if (_cachingEnabled)
                {
                    return _itemCache.Get(new CacheKey(kind, key));
                }
            }

            return direct();
        }

        public void Disable()
        {
            lock (_cacheLock)
            {
                _cachingEnabled = false;
                _itemCache.Clear();
                _allCache.Clear();
                _initCache.Clear();
            }
        }

        public void Dispose()
        {
            _itemCache?.Dispose();
            _allCache?.Dispose();
            _initCache?.Dispose();
        }
    }
}
