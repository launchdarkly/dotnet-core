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
                System.Collections.Immutable.ImmutableList<KeyValuePair<string, DataStoreTypes.ItemDescriptor>>.Builder>();

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
        /// Translates an FDv2 changeset with Full or None type into FDv1 PutData format.
        /// </summary>
        /// <param name="changeset">The changeset to translate.</param>
        /// <param name="log">Logger for diagnostic messages.</param>
        /// <returns>PutData containing the full dataset organized by data kind.</returns>
        public static StreamProcessorEvents.PutData TranslatePutData(FDv2ChangeSet changeset, Logger log)
        {
            var dataBuilder = System.Collections.Immutable.ImmutableList
                .CreateBuilder<KeyValuePair<DataStoreTypes.DataKind,
                    DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>>>();

            var changesByKind = changeset.Changes.GroupBy(c => c.Kind);

            foreach (var kindGroup in changesByKind)
            {
                var kind = kindGroup.Key;
                var dataKind = GetDataKind(kind);

                if (dataKind == null)
                {
                    log.Warn($"Unknown data kind '{kind}' in changeset, skipping");
                    continue;
                }

                var itemsBuilder = System.Collections.Immutable.ImmutableList
                    .CreateBuilder<KeyValuePair<string, DataStoreTypes.ItemDescriptor>>();

                foreach (var change in kindGroup)
                {
                    if (change.Type != FDv2ChangeType.Put || !change.Object.HasValue) continue;
                    var item = dataKind.DeserializeFromJsonElement(change.Object.Value);
                    itemsBuilder.Add(new KeyValuePair<string, DataStoreTypes.ItemDescriptor>(change.Key, item));
                    // Note: Delete operations in a Full changeset would be unusual, but we skip them
                    // since a full transfer should only contain items that exist
                }

                dataBuilder.Add(
                    new KeyValuePair<DataStoreTypes.DataKind, DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>>(
                        dataKind,
                        new DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>(itemsBuilder.ToImmutable())
                    ));
            }

            return new StreamProcessorEvents.PutData("/",
                new DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor>(dataBuilder.ToImmutable()));
        }

        /// <summary>
        /// Translates an FDv2 changeset with Partial type into a list of FDv1 PatchData format.
        /// </summary>
        /// <param name="changeset">The changeset to translate.</param>
        /// <param name="log">Logger for diagnostic messages.</param>
        /// <returns>List of PatchData representing individual upserts or deletes.</returns>
        public static List<StreamProcessorEvents.PatchData> TranslatePatchData(FDv2ChangeSet changeset, Logger log)
        {
            var patches = new List<StreamProcessorEvents.PatchData>();

            foreach (var change in changeset.Changes)
            {
                var dataKind = GetDataKind(change.Kind);

                if (dataKind == null)
                {
                    log.Warn($"Unknown data kind '{change.Kind}' in change, skipping");
                    continue;
                }

                DataStoreTypes.ItemDescriptor item;

                switch (change.Type)
                {
                    case FDv2ChangeType.Put when !change.Object.HasValue:
                        log.Warn($"Put operation for {change.Kind}/{change.Key} missing object data, skipping");
                        continue;
                    // Deserialize the object using the DataKind's deserializer
                    case FDv2ChangeType.Put:
                        item = dataKind.DeserializeFromJsonElement(change.Object.Value);
                        break;
                    case FDv2ChangeType.Delete:
                        // For deletes, create a deleted ItemDescriptor with the version
                        item = DataStoreTypes.ItemDescriptor.Deleted(change.Version);
                        break;
                    default:
                        log.Warn($"Unknown change type for {change.Kind}/{change.Key}, skipping");
                        continue;
                }

                patches.Add(new StreamProcessorEvents.PatchData(dataKind, change.Key, item));
            }

            return patches;
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
