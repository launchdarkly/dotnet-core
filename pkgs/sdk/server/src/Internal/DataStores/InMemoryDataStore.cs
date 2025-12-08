using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Server.Subsystems;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    /// <summary>
    /// In-memory, thread-safe implementation of <see cref="IDataStore"/>.
    /// </summary>
    /// <remarks>
    /// Application code cannot see this implementation class and uses
    /// <see cref="Components.InMemoryDataStore"/> instead.
    /// </remarks>
    internal class InMemoryDataStore : IDataStore, IDataStoreMetadata, ITransactionalDataStore
    {
        private readonly object WriterLock = new object();

        private volatile ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>> Items =
            ImmutableDictionary<DataKind, ImmutableDictionary<string, ItemDescriptor>>.Empty;

        private volatile bool _initialized = false;

        private volatile InitMetadata _metadata;

        private readonly object _selectorLock = new object();
        private Selector _selector;

        public Selector Selector
        {
            get
            {
                lock (_selectorLock)
                {
                    return _selector;
                }
            }
            private set
            {
                lock (_selectorLock)
                {
                    _selector = value;
                }
            }
        }

        internal InMemoryDataStore()
        {
            Selector = Selector.Empty;
        }

        public bool StatusMonitoringEnabled => false;

        public void Init(FullDataSet<ItemDescriptor> data)
        {
            InitWithMetadata(data, new InitMetadata());
        }

        public ItemDescriptor? Get(DataKind kind, string key)
        {
            if (!Items.TryGetValue(kind, out var itemsOfKind))
            {
                return null;
            }

            if (!itemsOfKind.TryGetValue(key, out var item))
            {
                return null;
            }

            return item;
        }

        public KeyedItems<ItemDescriptor> GetAll(DataKind kind)
        {
            if (Items.TryGetValue(kind, out var itemsOfKind))
            {
                return new KeyedItems<ItemDescriptor>(itemsOfKind);
            }

            return KeyedItems<ItemDescriptor>.Empty();
        }

        public bool Upsert(DataKind kind, string key, ItemDescriptor item)
        {
            lock (WriterLock)
            {
                if (!Items.TryGetValue(kind, out var itemsOfKind))
                {
                    itemsOfKind = ImmutableDictionary<string, ItemDescriptor>.Empty;
                }

                if (!itemsOfKind.TryGetValue(key, out var old) || old.Version < item.Version)
                {
                    var newItemsOfKind = itemsOfKind.SetItem(key, item);
                    Items = Items.SetItem(kind, newItemsOfKind);
                    return true;
                }

                return false;
            }
        }

        public void Apply(ChangeSet<ItemDescriptor> changeSet)
        {
            switch (changeSet.Type)
            {
                case ChangeSetType.Full:
                    ApplyFullPayload(changeSet.Data, new InitMetadata(changeSet.EnvironmentId), changeSet.Selector);
                    break;
                case ChangeSetType.Partial:
                    ApplyPartialData(changeSet.Data, changeSet.Selector);
                    break;
                case ChangeSetType.None:
                    break;
                default:
                    // This represents an implementation error. The ChangeSetType was extended, but handling was not
                    // added.
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool Initialized()
        {
            return _initialized;
        }

        public void Dispose()
        {
        }

        public void InitWithMetadata(FullDataSet<ItemDescriptor> data, InitMetadata metadata)
        {
            ApplyFullPayload(data.Data, metadata, Selector.Empty);
        }

        private void ApplyPartialData(IEnumerable<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>> data,
            Selector selector)
        {
            lock (WriterLock)
            {
                // Build the complete updated dictionary before assigning to Items for transactional update
                var itemsBuilder = Items.ToBuilder();

                foreach (var kindItemsPair in data)
                {
                    var kind = kindItemsPair.Key;
                    var kindBuilder = ImmutableDictionary.CreateBuilder<string, ItemDescriptor>();

                    if (!Items.TryGetValue(kind, out var itemsOfKind))
                    {
                        itemsOfKind = ImmutableDictionary<string, ItemDescriptor>.Empty;
                    }

                    kindBuilder.AddRange(itemsOfKind);

                    foreach (var keyValuePair in kindItemsPair.Value.Items)
                    {
                        kindBuilder[keyValuePair.Key] = keyValuePair.Value;
                    }

                    itemsBuilder[kind] = kindBuilder.ToImmutable();
                }

                Items = itemsBuilder.ToImmutable();
                Selector = selector;
            }
        }

        private void ApplyFullPayload(IEnumerable<KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>> data,
            InitMetadata metadata, Selector selector)
        {
            var itemsBuilder =
                ImmutableDictionary.CreateBuilder<DataKind, ImmutableDictionary<string, ItemDescriptor>>();

            foreach (var kindEntry in data)
            {
                var kindItemsBuilder = ImmutableDictionary.CreateBuilder<string, ItemDescriptor>();
                foreach (var e1 in kindEntry.Value.Items)
                {
                    kindItemsBuilder[e1.Key] = e1.Value;
                }

                itemsBuilder.Add(kindEntry.Key, kindItemsBuilder.ToImmutable());
            }

            var newItems = itemsBuilder.ToImmutable();

            lock (WriterLock)
            {
                Items = newItems;
                _metadata = metadata;
                _initialized = true;
                Selector = selector;
            }
        }

        public InitMetadata GetMetadata()
        {
            return _metadata;
        }
    }
}
