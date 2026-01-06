using System.Collections.Generic;
using System.Collections.Immutable;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataStores
{
    /// <summary>
    /// Utility for converting between in-memory and serialized data store formats.
    /// </summary>
    internal static class DataStoreConverter
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
                    SerializeAllItems(kind, items)
                ));
            }

            return new FullDataSet<SerializedItemDescriptor>(builder.ToImmutable());
        }

        /// <summary>
        /// Serializes all items of a given DataKind.
        /// </summary>
        private static KeyedItems<SerializedItemDescriptor> SerializeAllItems(
            DataKind kind,
            KeyedItems<ItemDescriptor> items)
        {
            var itemsBuilder = ImmutableList.CreateBuilder<
                KeyValuePair<string, SerializedItemDescriptor>>();

            foreach (var kv in items.Items)
            {
                var serializedItem = new SerializedItemDescriptor(
                    kv.Value.Version,
                    kv.Value.Item is null, // deleted flag
                    kind.Serialize(kv.Value)
                );
                itemsBuilder.Add(new KeyValuePair<string, SerializedItemDescriptor>(
                    kv.Key,
                    serializedItem
                ));
            }

            return new KeyedItems<SerializedItemDescriptor>(itemsBuilder.ToImmutable());
        }
    }
}
