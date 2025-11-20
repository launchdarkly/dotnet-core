using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// Translates FDv2 changesets into the format expected by the data store.
    /// </summary>
    internal static class FDv2ChangeSetTranslator
    {
        /// <summary>
        /// Translates an FDv2 changeset with Full or None type into PutData for initializing the data store.
        /// </summary>
        /// <param name="changeset">The changeset to translate.</param>
        /// <param name="log">Logger for diagnostic messages.</param>
        /// <returns>PutData containing the full dataset organized by data kind.</returns>
        public static StreamProcessorEvents.PutData TranslatePutData(FDv2ChangeSet changeset, Logger log)
        {
            var dataBuilder = System.Collections.Immutable.ImmutableList
                .CreateBuilder<KeyValuePair<DataStoreTypes.DataKind,
                    DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>>>();

            // Group changes by kind (flags vs segments)
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
                    if (change.Type != FDv2ChangeType.Put || change.Object == null) continue;
                    var item = dataKind.Deserialize(change.Object);
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
        /// Translates an FDv2 changeset with Partial type into a list of PatchData for incremental updates.
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

                if (change.Type == FDv2ChangeType.Put)
                {
                    if (change.Object == null)
                    {
                        log.Warn($"Put operation for {change.Kind}/{change.Key} missing object data, skipping");
                        continue;
                    }

                    // Deserialize the object using the DataKind's deserializer
                    item = dataKind.Deserialize(change.Object);
                }
                else if (change.Type == FDv2ChangeType.Delete)
                {
                    // For deletes, create a deleted ItemDescriptor with the version
                    item = DataStoreTypes.ItemDescriptor.Deleted(change.Version);
                }
                else
                {
                    log.Warn($"Unknown change type for {change.Kind}/{change.Key}, skipping");
                    continue;
                }

                patches.Add(new StreamProcessorEvents.PatchData(dataKind, change.Key, item));
            }

            return patches;
        }

        /// <summary>
        /// Maps an FDv2 kind string ("flag" or "segment") to the corresponding DataKind.
        /// </summary>
        /// <param name="kind">The kind string from the FDv2 change.</param>
        /// <returns>The corresponding DataKind, or null if the kind is not recognized.</returns>
        private static DataStoreTypes.DataKind GetDataKind(string kind)
        {
            if (kind == "flag")
            {
                return DataModel.Features;
            }

            if (kind == "segment")
            {
                return DataModel.Segments;
            }

            return null;
        }
    }
}
