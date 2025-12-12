using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    /// <summary>
    /// Adapts an <see cref="IDataSourceUpdatesV2"/> to work with data sources expecting <see cref="IDataSourceUpdates"/>.
    /// </summary>
    internal class DataSourceUpdatesV2ToV1Adapter : IDataSourceUpdates, IDataSourceUpdatesV2, IDataSourceUpdatesHeaders
    {
        private readonly IDataSourceUpdatesV2 _destination;

        public DataSourceUpdatesV2ToV1Adapter(IDataSourceUpdatesV2 sink)
        {
            _destination = sink;
        }

        public IDataStoreStatusProvider DataStoreStatusProvider => _destination.DataStoreStatusProvider;

        public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError) =>
            _destination.UpdateStatus(newState, newError);

        public bool Init(DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> allData)
        {
            return InitWithHeaders(allData, new List<KeyValuePair<string, IEnumerable<string>>>());
        }

        public bool Upsert(DataStoreTypes.DataKind kind, string key, DataStoreTypes.ItemDescriptor item)
        {
            // Create a single-item changeset for the upsert
            var items = ImmutableList.Create(new KeyValuePair<string, DataStoreTypes.ItemDescriptor>(key, item));
            var keyedItems = new DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>(items);
            var data = ImmutableList.Create(
                new KeyValuePair<DataStoreTypes.DataKind, DataStoreTypes.KeyedItems<DataStoreTypes.ItemDescriptor>>(
                    kind, keyedItems));

            var changeSet = new DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor>(
                DataStoreTypes.ChangeSetType.Partial,
                Subsystems.Selector.Empty,
                data,
                null
            );

            return _destination.Apply(changeSet);
        }

        public bool Apply(DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> changeSet) =>
            _destination.Apply(changeSet);

        public bool InitWithHeaders(DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> allData,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            // Extract environment ID from headers
            string environmentId = null;
            if (headers != null)
            {
                environmentId = headers.FirstOrDefault(item =>
                        item.Key.ToLower() == HeaderConstants.EnvironmentId).Value
                    ?.FirstOrDefault();
            }

            // Convert FullDataSet to ChangeSet and call Apply
            var changeSet = new DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor>(
                DataStoreTypes.ChangeSetType.Full,
                Subsystems.Selector.Empty,
                allData.Data,
                environmentId
            );

            return _destination.Apply(changeSet);
        }
    }
}
