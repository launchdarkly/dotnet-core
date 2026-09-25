using System.Collections.Immutable;
using LaunchDarkly.Sdk.Server.Internal.Model;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    /// <summary>
    /// The override store: a thread-safe collection of override entries, keyed by data kind and key,
    /// that is replaced wholesale on each update from an override source.
    /// </summary>
    /// <remarks>
    /// The contents are an immutable map that is swapped on update, so a reader always sees exactly
    /// one snapshot. Each flag or segment is stored as a marked copy. The copy shares its immutable
    /// parts with the caller's entity, and the caller's entity is never marked. A source may retain
    /// the entities it supplied and supply them again.
    /// </remarks>
    internal sealed class OverrideLayer
    {
        internal static readonly ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> EmptyContents =
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>>.Empty;

        private static readonly ImmutableDictionary<string, ItemDescriptor> NoItems =
            ImmutableDictionary<string, ItemDescriptor>.Empty;

        private readonly object _writeLock = new object();
        private volatile ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> _contents = EmptyContents;

        /// <summary>
        /// True if the layer holds no entries.
        /// </summary>
        internal bool IsEmpty
        {
            get
            {
                foreach (var kv in _contents)
                {
                    if (kv.Value.Count > 0)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Atomically replaces the entire layer contents. An empty data set clears the layer.
        /// </summary>
        /// <param name="data">the new contents</param>
        /// <param name="previous">receives the contents before the replacement</param>
        /// <param name="current">receives the contents after the replacement</param>
        internal void SetAll(
            FullDataSet<ItemDescriptor> data,
            out ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> previous,
            out ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> current
            )
        {
            var builder = ImmutableDictionary.CreateBuilder<DataKind, ImmutableDictionary<string, ItemDescriptor>>();
            foreach (var kindAndItems in data.Data)
            {
                var items = ImmutableDictionary.CreateBuilder<string, ItemDescriptor>();
                foreach (var keyAndItem in kindAndItems.Value.Items)
                {
                    items[keyAndItem.Key] = MarkedCopy(keyAndItem.Value);
                }
                builder[kindAndItems.Key] = items.ToImmutable();
            }
            var replacement = builder.ToImmutable();
            lock (_writeLock)
            {
                previous = _contents;
                _contents = replacement;
            }
            current = replacement;
        }

        /// <summary>
        /// Returns the override entry for a key, or null if the layer has none.
        /// </summary>
        internal ItemDescriptor? Get(DataKind kind, string key)
        {
            if (_contents.TryGetValue(kind, out var items) && items.TryGetValue(key, out var item))
            {
                return item;
            }
            return null;
        }

        /// <summary>
        /// Returns the entries of the given kind. The result is an immutable snapshot.
        /// </summary>
        internal ImmutableDictionary<string, ItemDescriptor> All(DataKind kind) =>
            _contents.TryGetValue(kind, out var items) ? items : NoItems;

        // Returns a copy of the item whose entity carries the override marker. An item of another
        // type, or a deleted-item placeholder, is returned as is.
        internal static ItemDescriptor MarkedCopy(ItemDescriptor item)
        {
            switch (item.Item)
            {
                case FeatureFlag flag:
                    return new ItemDescriptor(item.Version, flag.AsOverride());
                case Segment segment:
                    return new ItemDescriptor(item.Version, segment.AsOverride());
                default:
                    return item;
            }
        }
    }
}
