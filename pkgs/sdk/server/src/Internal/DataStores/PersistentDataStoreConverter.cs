using System.Collections.Generic;
using System.Collections.Immutable;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    /// <summary>
    /// Utility for converting between in-memory and serialized persistent data store formats.
    /// </summary>
    internal static class PersistentDataStoreConverter
    {
        /// <summary>
        /// Converts a FullDataSet of ItemDescriptor to SerializedItemDescriptor format.
        /// </summary>
        /// <param name="inMemoryData">The in-memory data to convert</param>
        /// <returns>A FullDataSet in serialized format suitable for persistent stores</returns>
        public static FullDataSet<SerializedItemDescriptor> ToSerializedFormat(
            FullDataSet<ItemDescriptor> inMemoryData)
        {
            var builder = ImmutableList.CreateBuilder<
                KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>>();

            foreach (var kindEntry in inMemoryData.Data)
            {
                var kind = kindEntry.Key;
                var items = kindEntry.Value;

                builder.Add(new KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>(
                    kind,
                    SerializeAll(kind, items.Items)
                ));
            }

            return new FullDataSet<SerializedItemDescriptor>(builder.ToImmutable());
        }

        /// <summary>
        /// Serializes a single item descriptor.
        /// </summary>
        /// <param name="kind">The data kind</param>
        /// <param name="itemDesc">The item descriptor to serialize</param>
        /// <returns>A serialized item descriptor</returns>
        public static SerializedItemDescriptor Serialize(DataKind kind, ItemDescriptor itemDesc)
        {
            return new SerializedItemDescriptor(itemDesc.Version,
                itemDesc.Item is null, kind.Serialize(itemDesc));
        }

        /// <summary>
        /// Serializes all items of a given DataKind from an enumerable collection.
        /// </summary>
        /// <param name="kind">The data kind</param>
        /// <param name="items">The items to serialize</param>
        /// <returns>Keyed items in serialized format</returns>
        public static KeyedItems<SerializedItemDescriptor> SerializeAll(
            DataKind kind,
            IEnumerable<KeyValuePair<string, ItemDescriptor>> items)
        {
            var itemsBuilder = ImmutableList.CreateBuilder<
                KeyValuePair<string, SerializedItemDescriptor>>();

            foreach (var kv in items)
            {
                itemsBuilder.Add(new KeyValuePair<string, SerializedItemDescriptor>(
                    kv.Key,
                    Serialize(kind, kv.Value)
                ));
            }

            return new KeyedItems<SerializedItemDescriptor>(itemsBuilder.ToImmutable());
        }

        /// <summary>
        /// Deserializes a single item descriptor.
        /// </summary>
        /// <param name="kind">The data kind</param>
        /// <param name="serializedItemDesc">The serialized item descriptor</param>
        /// <returns>A deserialized item descriptor</returns>
        public static ItemDescriptor Deserialize(DataKind kind, SerializedItemDescriptor serializedItemDesc)
        {
            if (serializedItemDesc.Deleted || serializedItemDesc.SerializedItem is null)
            {
                return ItemDescriptor.Deleted(serializedItemDesc.Version);
            }
            var deserializedItem = kind.Deserialize(serializedItemDesc.SerializedItem);
            if (serializedItemDesc.Version == 0 || serializedItemDesc.Version == deserializedItem.Version
                || deserializedItem.Item is null)
            {
                return deserializedItem;
            }
            // If the store gave us a version number that isn't what was encoded in the object, trust it
            return new ItemDescriptor(serializedItemDesc.Version, deserializedItem.Item);
        }
    }
}
