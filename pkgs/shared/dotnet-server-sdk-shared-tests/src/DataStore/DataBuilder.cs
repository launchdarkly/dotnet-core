using System;
using System.Collections.Generic;
using System.Linq;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    /// <summary>
    /// Simplifies building the input parameter for a data store's Init method.
    /// </summary>
    public class DataBuilder
    {
        private readonly IDictionary<DataKind, IDictionary<String, TestEntity>> _data =
            new Dictionary<DataKind, IDictionary<String, TestEntity>>();

        public DataBuilder Add(DataKind kind, params TestEntity[] items)
        {
            IDictionary<String, TestEntity> itemsDict;
            if (!_data.TryGetValue(kind, out itemsDict))
            {
                itemsDict = new Dictionary<String, TestEntity>();
                _data[kind] = itemsDict;
            }
            foreach (var item in items)
            {
                itemsDict[item.Key] = item;
            }
            return this;
        }

        public FullDataSet<SerializedItemDescriptor> BuildSerialized()
        {
            return new FullDataSet<SerializedItemDescriptor>(
                _data.ToDictionary(kv => kv.Key,
                    kv => new KeyedItems<SerializedItemDescriptor>(
                        kv.Value.ToDictionary(kv1 => kv1.Key,
                        kv1 => kv1.Value.SerializedItemDescriptor
                        )
                    )
                ));
        }
    }
}
