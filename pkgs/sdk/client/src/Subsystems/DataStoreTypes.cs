using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using static LaunchDarkly.Sdk.Client.DataModel;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    /// <summary>
    /// Types that are used by the data store and related interfaces.
    /// </summary>
    public static class DataStoreTypes
    {
        /// <summary>
        /// A versioned item (or placeholder) storeable in a data store.
        /// </summary>
        public struct ItemDescriptor : IEquatable<ItemDescriptor>
        {
            /// <summary>
            /// The version number of this data, provided by the SDK.
            /// </summary>
            public int Version { get; }

            /// <summary>
            /// The data item, or null if this is a deleted item placeholder.
            /// </summary>
            public FeatureFlag Item { get; }

            /// <summary>
            /// Constructs an instance.
            /// </summary>
            /// <param name="version">the version number</param>
            /// <param name="item">the data item, or null if this is a deleted item placeholder</param>
            public ItemDescriptor(int version, FeatureFlag item)
            {
                Version = version;
                Item = item;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) =>
                obj is ItemDescriptor o && Equals(o);


            /// <inheritdoc/>
            public bool Equals(ItemDescriptor other) =>
                other.Version == this.Version &&
                object.Equals(other.Item, this.Item);

            /// <inheritdoc/>
            public override int GetHashCode() =>
                Version + 31 * (Item?.GetHashCode() ?? 0);

            /// <inheritdoc/>
            public override string ToString() => "ItemDescriptor(" + Version + "," + Item + ")";
        }

        /// <summary>
        /// Represents a full set of feature flag data received from LaunchDarkly.
        /// </summary>
        public struct FullDataSet : IEquatable<FullDataSet>
        {
            /// <summary>
            /// The feature flag data.
            /// </summary>
            public IImmutableList<KeyValuePair<string, ItemDescriptor>> Items { get; }

            /// <summary>
            /// Creates a new instance.
            /// </summary>
            /// <param name="items">the feature flags, indexed by key</param>
            public FullDataSet(IEnumerable<KeyValuePair<string, ItemDescriptor>> items)
            {
                Items = items is null ? ImmutableList<KeyValuePair<string, ItemDescriptor>>.Empty :
                    ImmutableList.CreateRange(items);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) =>
                obj is FullDataSet o && Equals(o);


            /// <inheritdoc/>
            public bool Equals(FullDataSet other) =>
                Enumerable.SequenceEqual(other.Items.OrderBy(ItemKey), this.Items.OrderBy(ItemKey));

            /// <inheritdoc/>
            public override int GetHashCode() => Items.OrderBy(ItemKey).GetHashCode();

            private static string ItemKey(KeyValuePair<string, ItemDescriptor> kv) => kv.Key;
        }
    }
}
