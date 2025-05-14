using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Consul;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Internal implementation of the Consul data store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementation notes:
    /// </para>
    /// <list type="bullet">
    /// <item> Feature flags, segments, and any other kind of entity the LaunchDarkly client may wish
    /// to store, are stored as individual items with the key "{prefix}/features/{flag-key}",
    /// "{prefix}/segments/{segment-key}", etc.</item>
    /// <item> The special key "{prefix}/$inited" indicates that the store contains a complete data set.</item>
    /// <item> Since Consul has limited support for transactions(they can't contain more than 64
    /// operations), the Init method-- which replaces the entire data store-- is not guaranteed to
    /// be atomic, so there can be a race condition if another process is adding new data via
    /// Upsert. To minimize this, we don't delete all the data at the start; instead, we update
    /// the items we've received, and then delete all other items. That could potentially result in
    /// deleting new data from another process, but that would be the case anyway if the Init
    /// happened to execute later than the Upsert; we are relying on the fact that normally the
    /// process that did the Init will also receive the new data shortly and do its own Upsert.</item>
    /// </list>
    /// </remarks>
    internal sealed class ConsulDataStoreImpl : IPersistentDataStoreAsync
    {   
        private readonly ConsulClient _client;
        private readonly bool _wasExistingClient;
        private readonly string _prefix;
        private readonly Logger _log;
        
        internal ConsulDataStoreImpl(
            ConsulClient client,
            bool wasExistingClient,
            string prefix,
            Logger log
            )
        {
            _client = client;
            _wasExistingClient = wasExistingClient;
            _prefix = String.IsNullOrEmpty(prefix) ? "" : (prefix + "/");
            _log = log;
            _log.Info("Using Consul data store at {0} with prefix \"{1}\"",
                client.Config.Address, prefix);
        }
        
        public async Task<bool> InitializedAsync()
        {
            var result = await _client.KV.Get(InitedKey);
            return result.Response != null;
        }

        public async Task InitAsync(FullDataSet<SerializedItemDescriptor> allData)
        {
            // Start by reading the existing keys; we will later delete any of these that weren't in allData.
            var keysResult = await _client.KV.Keys(_prefix);
            var unusedOldKeys = keysResult.Response == null ? new HashSet<string>() :
                new HashSet<string>(keysResult.Response);

            var ops = new List<KVTxnOp>();
            var numItems = 0;

            // Insert or update every provided item
            foreach (var collection in allData.Data)
            {
                var kind = collection.Key;
                foreach (var keyAndItem in collection.Value.Items)
                {
                    var key = ItemKey(kind, keyAndItem.Key);
                    var op = new KVTxnOp(key, KVTxnVerb.Set)
                    {
                        Value = Encoding.UTF8.GetBytes(keyAndItem.Value.SerializedItem)
                    };
                    ops.Add(op);
                    unusedOldKeys.Remove(key);
                    numItems++;
                }
            }

            // Now delete any previously existing items whose keys were not in the current data
            foreach (var oldKey in unusedOldKeys)
            {
                ops.Add(new KVTxnOp(oldKey, KVTxnVerb.Delete));
            }

            // Now set the special key that we check in InitializedInternalAsync()
            var initedOp = new KVTxnOp(InitedKey, KVTxnVerb.Set)
            {
                Value = new byte[0]
            };
            ops.Add(initedOp);

            await BatchOperationsAsync(ops);

            _log.Info("Initialized data store with {0} items", numItems);
        }

        public async Task<SerializedItemDescriptor?> GetAsync(DataKind kind, string key)
        {
            var result = await _client.KV.Get(ItemKey(kind, key));
            return result.Response == null ? (SerializedItemDescriptor?)null :
                new SerializedItemDescriptor(0, false, Encoding.UTF8.GetString(result.Response.Value));
        }
        
        public async Task<KeyedItems<SerializedItemDescriptor>> GetAllAsync(DataKind kind)
        {
            var items = new List<KeyValuePair<string, SerializedItemDescriptor>>();
            var baseKey = KindKey(kind);
            var result = await _client.KV.List(baseKey);
            foreach (var pair in result.Response)
            {
                var itemKey = pair.Key.Substring(baseKey.Length + 1);
                items.Add(new KeyValuePair<string, SerializedItemDescriptor>(itemKey,
                    new SerializedItemDescriptor(0, false, Encoding.UTF8.GetString(pair.Value))));
            }
            return new KeyedItems<SerializedItemDescriptor>(items);
        }

        public async Task<bool> UpsertAsync(DataKind kind, string key, SerializedItemDescriptor newItem)
        {
            var fullKey = ItemKey(kind, key);

            // We will potentially keep retrying indefinitely until someone's write succeeds
            while (true)
            {
                var oldValue = (await _client.KV.Get(fullKey)).Response;
                var oldVersion = oldValue is null ? 0 :
                    kind.Deserialize(Encoding.UTF8.GetString(oldValue.Value)).Version;

                // Check whether the item is stale. If so, don't do the update (and return the existing item to
                // FeatureStoreWrapper so it can be cached)
                if (oldVersion >= newItem.Version)
                {
                    return false;
                }

                // Otherwise, try to write. We will do a compare-and-set operation, so the write will only succeed if
                // the key's ModifyIndex is still equal to the previous value returned by getEvenIfDeleted. If the
                // previous ModifyIndex was zero, it means the key did not previously exist and the write will only
                // succeed if it still doesn't exist.
                var modifyIndex = oldValue == null ? 0 : oldValue.ModifyIndex;
                var pair = new KVPair(fullKey)
                {
                    Value = Encoding.UTF8.GetBytes(newItem.SerializedItem),
                    ModifyIndex = modifyIndex
                };
                var result = await _client.KV.CAS(pair);
                if (result.Response)
                {
                    return true;
                }

                // If we failed, retry the whole shebang
                _log.Debug("Concurrent modification detected, retrying");
            }
        }

        public async Task<bool> IsStoreAvailableAsync()
        {
            try
            {
                await InitializedAsync(); // don't care about the return value, just that it doesn't throw an exception
                return true;
            }
            catch
            { // don't care about exception class, since any exception means the Consul request couldn't be made
                return false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_wasExistingClient)
                {
                    _client.Dispose();
                }
            }
        }

        private string ItemKey(DataKind kind, string key) => KindKey(kind) + "/" + key;

        private string KindKey(DataKind kind) => _prefix + kind.Name;
        
        private string InitedKey => _prefix + "$inited";

        private async Task BatchOperationsAsync(List<KVTxnOp> ops)
        {
            int batchSize = 64; // Consul can only do this many at a time
            for (int i = 0; i < ops.Count; i += batchSize)
            {
                var batch = ops.GetRange(i, Math.Min(batchSize, ops.Count - i));
                await _client.KV.Txn(batch);
            }
        }
    }
}
