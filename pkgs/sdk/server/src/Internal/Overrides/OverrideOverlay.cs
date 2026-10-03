using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Internal.DataSystem;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    /// <summary>
    /// Merges an <see cref="OverrideLayer"/> over a base store at the store read boundary. A read for
    /// a key returns the override entry when one exists, and the base entry otherwise. Targeting
    /// rules, prerequisites, and segment matches behave identically for overridden and ordinary data
    /// because they are the same reads through the same boundary.
    /// </summary>
    internal sealed class OverrideOverlay : IReadOnlyStore
    {
        private readonly IReadOnlyStore _base;
        private readonly OverrideLayer _layer;

        internal OverrideOverlay(IReadOnlyStore baseStore, OverrideLayer layer)
        {
            _base = baseStore;
            _layer = layer;
        }

        /// <summary>
        /// Returns the override entry for the key if one exists, and otherwise delegates to the base
        /// store. This works when the base store is uninitialized, because an uninitialized base
        /// reports not found rather than failing.
        /// </summary>
        public ItemDescriptor? Get(DataKind kind, string key)
        {
            var item = _layer.Get(kind, key);
            if (item.HasValue)
            {
                return item;
            }
            return _base.Get(kind, key);
        }

        /// <summary>
        /// Returns the union of the base store's items and the layer's items. The override entry wins
        /// for any key present in both, including a key the base holds as a deleted-item placeholder.
        /// </summary>
        /// <remarks>
        /// When the base store fails and the layer holds entries, the result is the layer's entries
        /// alone. A per-key read serves those entries whatever the state of the base, so an all-flags
        /// read does the same. When the layer is empty, the base failure propagates.
        /// </remarks>
        public KeyedItems<ItemDescriptor> GetAll(DataKind kind)
        {
            var overrideItems = _layer.All(kind);
            KeyedItems<ItemDescriptor> baseItems;
            try
            {
                baseItems = _base.GetAll(kind);
            }
            catch (Exception) when (overrideItems.Count > 0)
            {
                return new KeyedItems<ItemDescriptor>(overrideItems);
            }
            if (overrideItems.Count == 0)
            {
                return baseItems;
            }

            var result = new List<KeyValuePair<string, ItemDescriptor>>();
            var seen = new HashSet<string>();
            foreach (var keyAndItem in baseItems.Items)
            {
                seen.Add(keyAndItem.Key);
                if (overrideItems.TryGetValue(keyAndItem.Key, out var overrideItem))
                {
                    result.Add(new KeyValuePair<string, ItemDescriptor>(keyAndItem.Key, overrideItem));
                }
                else
                {
                    result.Add(keyAndItem);
                }
            }
            foreach (var keyAndItem in overrideItems)
            {
                if (!seen.Contains(keyAndItem.Key))
                {
                    result.Add(keyAndItem);
                }
            }
            return new KeyedItems<ItemDescriptor>(result);
        }

        /// <summary>
        /// Delegates to the base store. The override layer never affects initialization status or
        /// data availability.
        /// </summary>
        public bool Initialized() => _base.Initialized();

        public InitMetadata GetMetadata() => _base.GetMetadata();
    }
}
