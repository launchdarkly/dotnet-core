using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Subsystems;
using StackExchange.Redis;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Internal implementation of the Redis data store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementation notes:
    /// </para>
    /// <list type="bullet">
    /// <item><description> Feature flags, segments, and any other kind of entity the LaunchDarkly client may wish
    /// to store, are stored as hash values with the main key "{prefix}:features", "{prefix}:segments",
    /// etc.</description></item>
    /// <item><description> Redis only allows a single string value per hash key, so there is no way to store the
    /// item metadata (version number and deletion status) separately from the value. The SDK understands
    /// that some data store implementations don't have that capability, so it will always pass us a
    /// serialized item string that contains the metadata in it, and we're allowed to return 0 as the
    /// version number of a queried item to indicate "you have to deserialize the item to find out the
    /// metadata".
    /// </description></item>
    /// <item><description> The special key "{prefix}:$inited" indicates that the store contains a complete data set.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal sealed class RedisDataStoreImpl : RedisStoreImplBase, IPersistentDataStore
    {
        // This is used for unit testing only
        internal Action _updateHook;

        private readonly string _initedKey;

        internal RedisDataStoreImpl(
            IConnectionMultiplexer redis,
            string prefix,
            Logger log
            ) : base(redis, prefix, log)
        {
            _initedKey = prefix + ":$inited";
        }

        public bool Initialized() =>
            _redis.GetDatabase().KeyExists(_initedKey);

        public void Init(FullDataSet<SerializedItemDescriptor> allData)
        {
            IDatabase db = _redis.GetDatabase();
            ITransaction txn = db.CreateTransaction();
            foreach (var collection in allData.Data)
            {
                string key = ItemsKey(collection.Key);
                txn.KeyDeleteAsync(key);
                foreach (var item in collection.Value.Items)
                {
                    txn.HashSetAsync(key, item.Key, item.Value.SerializedItem);
                    // Note, these methods are async because this Redis client treats all actions
                    // in a transaction as async - they are only sent to Redis when we execute the
                    // transaction. We don't need to await them.
                }
            }
            txn.StringSetAsync(_initedKey, "");
            txn.Execute();
        }
        
        public SerializedItemDescriptor? Get(DataKind kind, string key)
        {
            IDatabase db = _redis.GetDatabase();
            string json = db.HashGet(ItemsKey(kind), key);
            if (json == null)
            {
                _log.Debug("[get] Key: {0} not found in \"{1}\"", key, kind.Name);
                return null;
            }
            return new SerializedItemDescriptor(0, false, json); // see implementation notes
        }

        public KeyedItems<SerializedItemDescriptor> GetAll(DataKind kind)
        {
            IDatabase db = _redis.GetDatabase();
            HashEntry[] allEntries = db.HashGetAll(ItemsKey(kind));
            var result = new List<KeyValuePair<string, SerializedItemDescriptor>>();
            foreach (HashEntry entry in allEntries)
            {
                result.Add(new KeyValuePair<string, SerializedItemDescriptor>(entry.Name,
                    new SerializedItemDescriptor(0, false, entry.Value))); // see implementation notes
            }
            return new KeyedItems<SerializedItemDescriptor>(result);
        }

        public bool Upsert(DataKind kind, string key, SerializedItemDescriptor newItem)
        {
            IDatabase db = _redis.GetDatabase();
            string baseKey = ItemsKey(kind);
            while (true)
            {
                string oldData;
                try
                {
                    oldData = db.HashGet(baseKey, key);
                }
                catch (RedisTimeoutException e)
                {
                    _log.Error("Timeout in update when reading {0} from {1}: {2}", key, baseKey, e.ToString());
                    throw;
                }
                // Here, unfortunately, we have to deserialize the old item (if any) just to find
                // out its version number (see implementation notes).
                var oldVersion = (oldData is null) ? 0 : kind.Deserialize(oldData).Version;
                if (oldVersion >= newItem.Version)
                {
                    _log.Debug("Attempted to {0} key: {1} version: {2} with a version that is" +
                        " the same or older: {3} in \"{4}\"",
                        newItem.Deleted ? "delete" : "update",
                        key, oldVersion, newItem.Version, kind.Name);
                    return false;
                }

                // This hook is used only in unit tests
                _updateHook?.Invoke();

                // Note that transactions work a bit differently in StackExchange.Redis than in other
                // Redis clients. The same Redis connection is shared across all threads, so it can't
                // set a WATCH at the moment we start the transaction. Instead, it saves up all of
                // the actions we send during the transaction, and replays them all within a MULTI
                // when the transaction. AddCondition() is this client's way of doing a WATCH, and it
                // can only use an equality match on the whole value (which is unfortunate since a
                // serialized flag value could be fairly large).
                ITransaction txn = db.CreateTransaction();
                txn.AddCondition(oldData is null ? Condition.HashNotExists(baseKey, key) :
                    Condition.HashEqual(baseKey, key, oldData));

                txn.HashSetAsync(baseKey, key, newItem.SerializedItem);

                try
                {
                    bool success = txn.Execute();
                    if (!success)
                    {
                        // The watch was triggered, we should retry
                        _log.Debug("Concurrent modification detected, retrying");
                        continue;
                    }
                }
                catch (RedisTimeoutException e)
                {
                    _log.Error("Timeout on update of {0} in {1}: {2}", key, baseKey, e.ToString());
                    throw;
                }
                return true;
            }
        }

        public bool IsStoreAvailable()
        {
            try
            {
                Initialized(); // don't care about the return value, just that it doesn't throw an exception
                return true;
            }
            catch
            { // don't care about exception class, since any exception means the Redis request couldn't be made
                return false;
            }
        }

        private string ItemsKey(DataKind kind) => _prefix + ":" + kind.Name;
    }
}
