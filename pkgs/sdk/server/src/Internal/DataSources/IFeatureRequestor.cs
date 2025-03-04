using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    internal class DataSetWithHeaders
    {
        public readonly FullDataSet<ItemDescriptor>? DataSet;
        public readonly IEnumerable<KeyValuePair<string, IEnumerable<string>>> Headers;

        public DataSetWithHeaders(FullDataSet<ItemDescriptor>? dataSet,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            DataSet = dataSet;
            Headers = headers;
        }
    }

    internal interface IFeatureRequestor : IDisposable
    {
        Task<DataSetWithHeaders> GetAllDataAsync();
    }
}
