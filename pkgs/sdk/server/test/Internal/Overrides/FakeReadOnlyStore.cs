using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Internal.DataSystem;
using LaunchDarkly.Sdk.Server.Internal.Model;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    // A minimal read-only store for testing the overlay and the sink against arbitrary base data and
    // initialization states.
    internal sealed class FakeReadOnlyStore : IReadOnlyStore
    {
        internal readonly Dictionary<string, ItemDescriptor> Flags = new Dictionary<string, ItemDescriptor>();
        internal readonly Dictionary<string, ItemDescriptor> Segments = new Dictionary<string, ItemDescriptor>();
        internal bool IsInitialized = true;
        internal Exception GetAllError;
        internal int GetAllCalls;

        internal FakeReadOnlyStore WithFlags(params FeatureFlag[] flags)
        {
            foreach (var flag in flags)
            {
                Flags[flag.Key] = new ItemDescriptor(flag.Version, flag);
            }
            return this;
        }

        internal FakeReadOnlyStore WithSegments(params Segment[] segments)
        {
            foreach (var segment in segments)
            {
                Segments[segment.Key] = new ItemDescriptor(segment.Version, segment);
            }
            return this;
        }

        internal FakeReadOnlyStore WithDeletedFlag(string key, int version)
        {
            Flags[key] = ItemDescriptor.Deleted(version);
            return this;
        }

        private Dictionary<string, ItemDescriptor> ItemsOf(DataKind kind)
        {
            if (kind == DataModel.Features)
            {
                return Flags;
            }
            if (kind == DataModel.Segments)
            {
                return Segments;
            }
            return new Dictionary<string, ItemDescriptor>();
        }

        public ItemDescriptor? Get(DataKind kind, string key) =>
            ItemsOf(kind).TryGetValue(key, out var item) ? item : (ItemDescriptor?)null;

        public KeyedItems<ItemDescriptor> GetAll(DataKind kind)
        {
            GetAllCalls++;
            if (GetAllError != null)
            {
                throw GetAllError;
            }
            return new KeyedItems<ItemDescriptor>(new Dictionary<string, ItemDescriptor>(ItemsOf(kind)));
        }

        public bool Initialized() => IsInitialized;

        public InitMetadata GetMetadata() => null;
    }
}
