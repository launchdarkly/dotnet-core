using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Internal.DataSystem;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    /// <summary>
    /// Computes which flags an override layer replacement may have changed, so that flag change
    /// listeners hear about them the same as for a change from LaunchDarkly.
    /// </summary>
    internal static class OverrideChanges
    {
        private static readonly DataKind[] DiffKinds = { DataModel.Features, DataModel.Segments };

        /// <summary>
        /// Returns the keys of all flags whose merged-view evaluation may have changed when the
        /// override layer was replaced. The result includes the flags whose override entries were
        /// added, removed, or changed. Dependency fan-out adds every flag that depends, directly or
        /// transitively, on any added, removed, or changed entry of either kind.
        /// </summary>
        internal static ICollection<string> ComputeAffectedFlags(
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> oldOverrides,
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> newOverrides,
            IDictionary<DataKind, Dictionary<string, ItemDescriptor>> oldMerged,
            IDictionary<DataKind, Dictionary<string, ItemDescriptor>> newMerged
            )
        {
            var seeds = DiffOverrides(oldOverrides, newOverrides);
            if (seeds.Count == 0)
            {
                return new List<string>();
            }

            // Dependency edges are computed over both the old and the new merged views, because a
            // replacement can rewire dependencies. For example, removing a flag override restores the
            // prerequisite edges of the LaunchDarkly definition. Flags that depended on the override's
            // references exist as dependents only in the old view.
            var oldTracker = TrackerFromView(oldMerged);
            var newTracker = TrackerFromView(newMerged);
            var affected = new HashSet<KindAndKey>();
            foreach (var seed in seeds)
            {
                oldTracker.AddAffectedItems(affected, seed);
                newTracker.AddAffectedItems(affected, seed);
            }

            var flagKeys = new List<string>();
            foreach (var item in affected)
            {
                if (item.Kind == DataModel.Features)
                {
                    flagKeys.Add(item.Key);
                }
            }
            return flagKeys;
        }

        // Returns an entry for each key whose override entry differs between the two layer
        // snapshots. An added or removed entry is always a change, even when its content matches the
        // underlying LaunchDarkly data, because the override marker alone changes the served entry.
        // Entries present in both snapshots are compared by their serialized form. The layer is
        // rebuilt wholesale on every update, so reference or version comparison would report every
        // retained entry as changed.
        private static List<KindAndKey> DiffOverrides(
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> oldOverrides,
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> newOverrides
            )
        {
            var seeds = new List<KindAndKey>();
            foreach (var kind in DiffKinds)
            {
                var oldItems = ItemsOf(oldOverrides, kind);
                var newItems = ItemsOf(newOverrides, kind);
                foreach (var kv in oldItems)
                {
                    if (!newItems.TryGetValue(kv.Key, out var newItem) || !ItemsEqual(kind, kv.Value, newItem))
                    {
                        seeds.Add(new KindAndKey(kind, kv.Key));
                    }
                }
                foreach (var kv in newItems)
                {
                    if (!oldItems.ContainsKey(kv.Key))
                    {
                        seeds.Add(new KindAndKey(kind, kv.Key));
                    }
                }
            }
            return seeds;
        }

        private static ImmutableDictionary<string, ItemDescriptor> ItemsOf(
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> contents,
            DataKind kind
            ) =>
            contents.TryGetValue(kind, out var items) ? items : ImmutableDictionary<string, ItemDescriptor>.Empty;

        private static bool ItemsEqual(DataKind kind, ItemDescriptor a, ItemDescriptor b)
        {
            if (a.Version != b.Version)
            {
                return false;
            }
            return kind.Serialize(a) == kind.Serialize(b);
        }

        /// <summary>
        /// Captures the merged view of a base store and a layer snapshot: base data with the override
        /// entries overlaid. A base read failure for a kind yields just the overrides for that kind.
        /// This degrades the dependency fan-out but never loses the directly changed keys.
        /// </summary>
        internal static IDictionary<DataKind, Dictionary<string, ItemDescriptor>> SnapshotMergedView(
            IReadOnlyStore baseStore,
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> overrides
            )
        {
            var view = new Dictionary<DataKind, Dictionary<string, ItemDescriptor>>();
            foreach (var kind in DiffKinds)
            {
                var items = new Dictionary<string, ItemDescriptor>();
                try
                {
                    foreach (var kv in baseStore.GetAll(kind).Items)
                    {
                        items[kv.Key] = kv.Value;
                    }
                }
                catch (Exception)
                {
                    // The base store could not be read. The view holds only the overrides.
                }
                foreach (var kv in ItemsOf(overrides, kind))
                {
                    items[kv.Key] = kv.Value;
                }
                view[kind] = items;
            }
            return view;
        }

        private static DependencyTracker TrackerFromView(IDictionary<DataKind, Dictionary<string, ItemDescriptor>> view)
        {
            var tracker = new DependencyTracker();
            foreach (var kind in DiffKinds)
            {
                if (view.TryGetValue(kind, out var items))
                {
                    foreach (var kv in items)
                    {
                        tracker.UpdateDependenciesFrom(kind, kv.Key, kv.Value);
                    }
                }
            }
            return tracker;
        }
    }
}
