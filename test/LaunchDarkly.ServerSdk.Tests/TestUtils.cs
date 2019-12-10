﻿using System.Collections.Generic;
using LaunchDarkly.Sdk.Interfaces;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Xunit;

using static LaunchDarkly.Sdk.Server.Interfaces.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server
{
    public class TestUtils
    {
        public static void AssertJsonEqual(JToken expected, JToken actual)
        {
            if (!JToken.DeepEquals(expected, actual))
            {
                Assert.True(false,
                    string.Format("JSON result mismatch; expected {0}, got {1}",
                        JsonConvert.SerializeObject(expected),
                        JsonConvert.SerializeObject(actual)));
            }
        }

        public static string TestFilePath(string name)
        {
            return "./TestFiles/" + name;
        }
        
        internal static bool UpsertFlag(IDataStore store, FeatureFlag item)
        {
            return store.Upsert(DataKinds.Features, item.Key, new ItemDescriptor(item.Version, item));
        }

        internal static bool UpsertSegment(IDataStore store, Segment item)
        {
            return store.Upsert(DataKinds.Segments, item.Key, new ItemDescriptor(item.Version, item));
        }

        public static IDataStoreFactory SpecificDataStore(IDataStore store)
        {
            return new SpecificDataStoreFactory(store);
        }

        public static IEventProcessorFactory SpecificEventProcessor(IEventProcessor ep)
        {
            return new SpecificEventProcessorFactory(ep);
        }

        public static IDataSourceFactory SpecificDataSource(IDataSource up)
        {
            return new SpecificDataSourceFactory(up);
        }

        internal static IDataSourceFactory DataSourceWithData(FullDataSet<ItemDescriptor> data)
        {
            return new DataSourceFactoryWithData(data);
        }
    }

    public class SpecificDataStoreFactory : IDataStoreFactory
    {
        private readonly IDataStore _store;

        public SpecificDataStoreFactory(IDataStore store)
        {
            _store = store;
        }

        IDataStore IDataStoreFactory.CreateDataStore()
        {
            return _store;
        }
    }

    public class SpecificEventProcessorFactory : IEventProcessorFactory
    {
        private readonly IEventProcessor _ep;

        public SpecificEventProcessorFactory(IEventProcessor ep)
        {
            _ep = ep;
        }

        IEventProcessor IEventProcessorFactory.CreateEventProcessor(Configuration config)
        {
            return _ep;
        }
    }

    public class SpecificDataSourceFactory : IDataSourceFactory
    {
        private readonly IDataSource _ds;

        public SpecificDataSourceFactory(IDataSource ds)
        {
            _ds = ds;
        }

        IDataSource IDataSourceFactory.CreateDataSource(Configuration config, IDataStore dataStore)
        {
            return _ds;
        }
    }

    public class DataSourceFactoryWithData : IDataSourceFactory
    {
        private readonly FullDataSet<ItemDescriptor> _data;

        public DataSourceFactoryWithData(FullDataSet<ItemDescriptor> data)
        {
            _data = data;
        }

        public IDataSource CreateDataSource(Configuration config, IDataStore dataStore)
        {
            return new DataSourceWithData(dataStore, _data);
        }
    }

    public class DataSourceWithData : IDataSource
    {
        private readonly IDataStore _store;
        private readonly FullDataSet<ItemDescriptor> _data;

        public DataSourceWithData(IDataStore store, FullDataSet<ItemDescriptor> data)
        {
            _store = store;
            _data = data;
        }

        public Task<bool> Start()
        {
            _store.Init(_data);
            return Task.FromResult(true);
        }

        public bool Initialized()
        {
            return true;
        }
        
        public void Dispose() { }
    }

    public class TestEventProcessor : IEventProcessor
    {
        public List<Event> Events = new List<Event>();

        public void SendEvent(Event e)
        {
            Events.Add(e);
        }

        public void SetOffline(bool offline) { }

        public void Flush() { }

        public void Dispose() { }
    }
}
