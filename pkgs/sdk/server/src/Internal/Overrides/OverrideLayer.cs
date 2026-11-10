using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Internal.Model;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    /// <summary>
    /// The override store: a thread-safe collection of override entries, keyed by data kind and key,
    /// that is replaced wholesale on each update from an override source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contents are an immutable map that is swapped on update, so a reader always sees exactly
    /// one snapshot. Each flag or segment is stored as a marked copy. The copy shares its immutable
    /// parts with the caller's entity, and the caller's entity is never marked. A source may retain
    /// the entities it supplied and supply them again.
    /// </para>
    /// <para>
    /// The layer never holds a deleted item. An override entry defines a flag or segment; a deleted
    /// item in the layer would hide the LaunchDarkly data for its key, although overrides only add or
    /// replace definitions. <see cref="OverrideSink"/> rejects a snapshot that contains one before it
    /// reaches the layer, and <see cref="SetAll"/> refuses one.
    /// </para>
    /// </remarks>
    internal sealed class OverrideLayer : IExternalDependencies
    {
        internal static readonly ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> EmptyContents =
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>>.Empty;

        private static readonly ImmutableDictionary<string, ItemDescriptor> NoItems =
            ImmutableDictionary<string, ItemDescriptor>.Empty;

        private readonly object _writeLock = new object();
        private volatile ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> _contents = EmptyContents;

        // The dependency edges of the layer's own entries: which entries reference which flags and
        // segments. Rebuilt with the contents on every replacement and swapped with them.
        private volatile DependencyTracker _dependencies = new DependencyTracker();

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
        /// <exception cref="ArgumentException">the data set contains a deleted item (see
        /// <see cref="FindDeletedItem"/>); the layer is unchanged</exception>
        internal void SetAll(
            FullDataSet<ItemDescriptor> data,
            out ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> previous,
            out ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> current
            )
        {
            var builder = ImmutableDictionary.CreateBuilder<DataKind, ImmutableDictionary<string, ItemDescriptor>>();
            var dependencies = new DependencyTracker();
            foreach (var kindAndItems in data.Data)
            {
                var items = ImmutableDictionary.CreateBuilder<string, ItemDescriptor>();
                foreach (var keyAndItem in kindAndItems.Value.Items)
                {
                    if (IsDeleted(keyAndItem.Value))
                    {
                        // Nothing has been swapped in yet, so the layer is unchanged.
                        throw new ArgumentException(DescribeItem(kindAndItems.Key, keyAndItem.Key) +
                            " is marked as deleted; the override layer does not hold deleted items", nameof(data));
                    }
                    var copied = MarkedCopy(keyAndItem.Value);
                    items[keyAndItem.Key] = copied;
                    try
                    {
                        dependencies.UpdateDependenciesFrom(kindAndItems.Key, keyAndItem.Key, copied);
                    }
                    catch (Exception)
                    {
                        // A definition whose references cannot be read (a prerequisite without a key, a
                        // segmentMatch value that is not a string) still enters the layer; it evaluates to
                        // an error on its own. It contributes no edges, the same as the change
                        // computation in OverrideChanges skips it.
                    }
                }
                builder[kindAndItems.Key] = items.ToImmutable();
            }
            var replacement = builder.ToImmutable();
            lock (_writeLock)
            {
                previous = _contents;
                _contents = replacement;
                _dependencies = dependencies;
            }
            current = replacement;
        }

        /// <summary>
        /// Adds the given item to the set together with every item whose override entry depends on
        /// it, directly or through other override entries. A LaunchDarkly update uses this so that a
        /// change to a LaunchDarkly segment or prerequisite notifies the overridden flags that
        /// reference it, which the data system's own dependency tracking cannot see.
        /// </summary>
        public void AddAffectedItems(ISet<KindAndKey> itemsOut, KindAndKey item) =>
            _dependencies.AddAffectedItems(itemsOut, item);

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

        /// <summary>
        /// Finds the first deleted item in a data set: a deleted-item placeholder, whose item is null,
        /// or a flag or segment that is marked as deleted.
        /// </summary>
        /// <param name="data">the data set to search</param>
        /// <param name="kind">receives the kind of the deleted item, if there is one</param>
        /// <param name="key">receives the key of the deleted item, if there is one</param>
        /// <returns>true if the data set contains a deleted item</returns>
        internal static bool FindDeletedItem(FullDataSet<ItemDescriptor> data, out DataKind kind, out string key)
        {
            foreach (var kindAndItems in data.Data)
            {
                foreach (var keyAndItem in kindAndItems.Value.Items)
                {
                    if (IsDeleted(keyAndItem.Value))
                    {
                        kind = kindAndItems.Key;
                        key = keyAndItem.Key;
                        return true;
                    }
                }
            }
            kind = null;
            key = null;
            return false;
        }

        /// <summary>
        /// Describes an item for a log message, for example <c>flag "my-flag"</c>.
        /// </summary>
        internal static string DescribeItem(DataKind kind, string key)
        {
            string category;
            if (kind == DataModel.Features)
            {
                category = "flag";
            }
            else if (kind == DataModel.Segments)
            {
                category = "segment";
            }
            else
            {
                category = kind.Name;
            }
            return category + " \"" + key + "\"";
        }

        // DataKind.Deserialize turns a definition marked as deleted into a placeholder with no item. A
        // definition that reaches the layer some other way can still carry the deleted property.
        private static bool IsDeleted(ItemDescriptor item)
        {
            switch (item.Item)
            {
                case null:
                    return true;
                case FeatureFlag flag:
                    return flag.Deleted;
                case Segment segment:
                    return segment.Deleted;
                default:
                    return false;
            }
        }

        // Returns a copy of the item whose entity carries the override marker. An item of another
        // type is returned as is.
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
