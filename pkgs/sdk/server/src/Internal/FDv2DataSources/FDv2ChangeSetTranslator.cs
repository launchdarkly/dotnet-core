using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// Translates FDv2 changesets into data store formats.
    /// </summary>
    internal static class FDv2ChangeSetTranslator
    {
        /// <summary>
        /// Converts an FDv2ChangeSet to a DataStoreTypes.ChangeSet.
        /// </summary>
        /// <param name="changeset">The FDv2 changeset to convert.</param>
        /// <param name="log">Logger for diagnostic messages.</param>
        /// <param name="environmentId">The environment ID to include in the changeset.</param>
        /// <returns>A DataStoreTypes.ChangeSet containing the converted data.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the changeset type is unknown.</exception>
        public static DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> ToChangeSet(
            FDv2ChangeSet changeset,
            Logger log,
            string environmentId = null)
        {
            DataStoreTypes.ChangeSetType changeSetType;
            switch (changeset.Type)
            {
                case FDv2ChangeSetType.Full:
                    changeSetType = DataStoreTypes.ChangeSetType.Full;
                    break;
                case FDv2ChangeSetType.Partial:
                    changeSetType = DataStoreTypes.ChangeSetType.Partial;
                    break;
                case FDv2ChangeSetType.None:
                    changeSetType = DataStoreTypes.ChangeSetType.None;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(changeset),
                        $"Unknown FDv2ChangeSetType: {changeset.Type}. This is an implementation error.");
            }

            // Use a dictionary to group items by DataKind in a single pass
            var kindToItems = new Dictionary<DataStoreTypes.DataKind,
                System.Collections.Immutable.ImmutableList<KeyValuePair<string, DataStoreTypes.ItemDescriptor>>.
                Builder>();

            foreach (var change in changeset.Changes)
            {
                var dataKind = GetDataKind(change.Kind);

                if (dataKind == null)
                {
                    log.Warn($"Unknown data kind '{change.Kind}' in changeset, skipping");
                    continue;
                }

                DataStoreTypes.ItemDescriptor item;

                switch (change.Type)
                {
                    case FDv2ChangeType.Put when !change.Object.HasValue:
                        log.Warn($"Put operation for {change.Kind}/{change.Key} missing object data, skipping");
                        continue;
                    case FDv2ChangeType.Put:
                        item = dataKind.DeserializeFromJsonElement(change.Object.Value);
                        break;
                    case FDv2ChangeType.Delete:
                        item = DataStoreTypes.ItemDescriptor.Deleted(change.Version);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change),
                            $"Unknown FDv2ChangeType: {change.Type}. This is an implementation error.");
                }

                if (!kindToItems.TryGetValue(dataKind, out var itemsBuilder))
                {
                    itemsBuilder = System.Collections.Immutable.ImmutableList
                        .CreateBuilder<KeyValuePair<string, DataStoreTypes.ItemDescriptor>>();
                    kindToItems[dataKind] = itemsBuilder;
                }

                itemsBuilder.Add(new KeyValuePair<string, DataStoreTypes.ItemDescriptor>(change.Key, item));
            }

            var dataBuilder = System.Collections.Immutable.ImmutableList
                .CreateBuilder<KeyValuePair<DataStoreTypes.DataKind,
                    DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>>>();

            foreach (var kvp in kindToItems)
            {
                dataBuilder.Add(
                    new KeyValuePair<DataStoreTypes.DataKind, DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>>(
                        kvp.Key,
                        new DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>(kvp.Value.ToImmutable())
                    ));
            }

            return new DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor>(
                changeSetType,
                changeset.Selector,
                dataBuilder.ToImmutable(),
                environmentId);
        }

        /// <summary>
        /// Maps an FDv2 object kind to the corresponding DataKind.
        /// </summary>
        /// <param name="kind">The kind string from the FDv2 change.</param>
        /// <returns>The corresponding DataKind, or null if the kind is not recognized.</returns>
        private static DataStoreTypes.DataKind GetDataKind(string kind)
        {
            switch (kind)
            {
                case "flag":
                    return DataModel.Features;
                case "segment":
                    return DataModel.Segments;
                default:
                    return null;
            }
        }
    }
}
