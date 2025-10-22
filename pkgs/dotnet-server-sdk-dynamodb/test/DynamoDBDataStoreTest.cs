using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.Sdk.Server.SharedTests.DataStore;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Integrations.DynamoDBTestEnvironment;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    [Collection("DynamoDB Tests")]
    public class DynamoDBDataStoreTest : PersistentDataStoreBaseTests, IAsyncLifetime
    {
        const string BadItemKey = "baditem";

        public DynamoDBDataStoreTest(ITestOutputHelper testOutput) : base(testOutput) { }

        protected override PersistentDataStoreTestConfig Configuration =>
            new PersistentDataStoreTestConfig
            {
                StoreAsyncFactoryFunc = MakeStoreFactory,
                ClearDataAction = ClearAllData
            };


        public Task InitializeAsync() => CreateTableIfNecessary();

        public Task DisposeAsync() => Task.CompletedTask;

        private IComponentConfigurer<IPersistentDataStoreAsync> MakeStoreFactory(string prefix) =>
            BaseBuilder().Prefix(prefix);

        private DynamoDBStoreBuilder<IPersistentDataStoreAsync> BaseBuilder() =>
            DynamoDB.DataStore(TableName)
                .Credentials(MakeTestCredentials())
                .Configuration(MakeTestConfiguration());

        [Fact]
        public void LogMessageAtStartup()
        {
            var logCapture = Logs.Capture();
            var logger = logCapture.Logger("BaseLoggerName"); // in real life, the SDK will provide its own base log name
            var context = new LdClientContext("", null, null, null, logger, false, null);
            using (((IComponentConfigurer<IPersistentDataStoreAsync>)BaseBuilder().Prefix("my-prefix")).Build(context))
            {
                Assert.Collection(logCapture.GetMessages(),
                    m =>
                    {
                        Assert.Equal(LaunchDarkly.Logging.LogLevel.Info, m.Level);
                        Assert.Equal("BaseLoggerName.DataStore.DynamoDB", m.LoggerName);
                        Assert.Equal("Using DynamoDB data store with table name \"" + TableName +
                            "\" and prefix \"my-prefix\"", m.Text);
                    });
            }
        }

        [Theory]
        [InlineData("flag")]
        [InlineData("segment")]
        public async void DataStoreSkipsAndLogsTooLargeItemOnInit(string flagOrSegment)
        {
            var dataPlusBadItem = MakeGoodData().Data.ToList();
            GetTooLargeItemParams(flagOrSegment, out var dataKind, out var collIndex, out SerializedItemDescriptor item);
            var items = dataPlusBadItem[collIndex].Value.Items.ToList();
            items.Insert(0, new KeyValuePair<string, SerializedItemDescriptor>(BadItemKey, item));
            // put the bad item first to prove that items after that one are still stored
            dataPlusBadItem[collIndex] = new KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>(
                dataKind, new KeyedItems<SerializedItemDescriptor>(items));

            var logCapture = Logs.Capture();
            var context = new LdClientContext("", null, null, null, logCapture.Logger(""), false, null);

            using (var store = ((IComponentConfigurer<IPersistentDataStoreAsync>)BaseBuilder()).Build(context))
            {
                await store.InitAsync(new FullDataSet<SerializedItemDescriptor>(dataPlusBadItem));

                Assert.True(logCapture.HasMessageWithRegex(LogLevel.Error,
                    @"""" + BadItemKey + @""".*was too large to store in DynamoDB and was dropped"));

                AssertDataSetsEqual(MakeGoodData(), await GetAllData(store));
            }
        }

        [Theory]
        [InlineData("flag")]
        [InlineData("segment")]
        public async void DataStoreSkipsAndLogsTooLargeItemOnUpsert(string flagOrSegment)
        {
            var goodData = MakeGoodData();
            GetTooLargeItemParams(flagOrSegment, out var dataKind, out var collIndex, out SerializedItemDescriptor item);

            var logCapture = Logs.Capture();
            var context = new LdClientContext("", null, null, null, logCapture.Logger(""), false, null);

            using (var store = ((IComponentConfigurer<IPersistentDataStoreAsync>)BaseBuilder()).Build(context))
            {
                await store.InitAsync(goodData);

                AssertDataSetsEqual(MakeGoodData(), await GetAllData(store));

                await store.UpsertAsync(dataKind, BadItemKey, item);

                Assert.True(logCapture.HasMessageWithRegex(LogLevel.Error,
                    @"""" + BadItemKey + @""".*was too large to store in DynamoDB and was dropped"));

                AssertDataSetsEqual(MakeGoodData(), await GetAllData(store));
            }
        }

        private static FullDataSet<SerializedItemDescriptor> MakeGoodData()
        {
            // Not using the DataBuilder helper because that currently does not preserve insertion order.
            var list = new List<KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>>
            {
                new KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>(
                    DataModel.Features,
                    new KeyedItems<SerializedItemDescriptor>(
                        new List<KeyValuePair<string, SerializedItemDescriptor>>
                        {
                            new KeyValuePair<string, SerializedItemDescriptor>(
                                "flag1",
                                new SerializedItemDescriptor(1, false, @"{""key"": ""flag1"", ""version"": 1}")
                            ),
                            new KeyValuePair<string, SerializedItemDescriptor>(
                                "flag2",
                                new SerializedItemDescriptor(1, false, @"{""key"": ""flag2"", ""version"": 1}")
                            )
                        })),
                new KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>(
                    DataModel.Segments,
                    new KeyedItems<SerializedItemDescriptor>(
                        new List<KeyValuePair<string, SerializedItemDescriptor>>
                        {
                            new KeyValuePair<string, SerializedItemDescriptor>(
                                "segment1",
                                new SerializedItemDescriptor(1, false, @"{""key"": ""segment1"", ""version"": 1}")
                            ),
                            new KeyValuePair<string, SerializedItemDescriptor>(
                                "segment2",
                                new SerializedItemDescriptor(1, false, @"{""key"": ""segment2"", ""version"": 1}")
                            )
                        }))
            };
            return new FullDataSet<SerializedItemDescriptor>(list);
        }

        private static async Task<FullDataSet<SerializedItemDescriptor>> GetAllData(IPersistentDataStoreAsync store)
        {
            var colls = new List<KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>>();
            foreach (var kind in new DataKind[] { DataModel.Features, DataModel.Segments })
            {
                colls.Add(new KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>(
                    kind, await store.GetAllAsync(kind)));
            }
            return new FullDataSet<SerializedItemDescriptor>(colls);
        }

        private static void GetTooLargeItemParams(
            string flagOrSegment,
            out DataKind dataKind,
            out int collIndex,
            out SerializedItemDescriptor item
            )
        {
            string tooBigKeysListJson = "[";
            for (var i = 0; i < 40000; i++)
            {
                if (i != 0)
                {
                    tooBigKeysListJson += ",";
                }
                tooBigKeysListJson += @"""key" + i + @"""";
            }
            tooBigKeysListJson += "]";
            Assert.NotInRange(tooBigKeysListJson.Length, 0, 400 * 1024);

            string badItemJson;
            switch (flagOrSegment)
            {
                case "flag":
                    dataKind = DataModel.Features;
                    collIndex = 0;
                    badItemJson = @"{""key"":""" + BadItemKey + @""", ""version"": 1, ""targets"":[{""variation"":0,""values"":" +
                        tooBigKeysListJson + "}]}";
                    break;

                case "segment":
                    dataKind = DataModel.Segments;
                    collIndex = 1;
                    badItemJson = @"{""key"":""" + BadItemKey + @""", ""version"": 1, ""included"":" + tooBigKeysListJson + "]}";
                    break;

                default:
                    throw new ArgumentException("invalid type parameter");
            }
            item = new SerializedItemDescriptor(1, false, badItemJson);
        }

        private static void AssertDataSetsEqual(FullDataSet<SerializedItemDescriptor> expected,
            FullDataSet<SerializedItemDescriptor> actual)
        {
            var collMatchers = new List<Action<KeyValuePair<DataKind, KeyedItems<SerializedItemDescriptor>>>>();
            foreach (var expectedColl in expected.Data)
            {
                collMatchers.Add(actualColl =>
                {
                    Assert.Equal(expectedColl.Key, actualColl.Key);
                    var itemsMatchers = new List<Action<KeyValuePair<string, SerializedItemDescriptor>>>();
                    foreach (var expectedKV in expectedColl.Value.Items)
                    {
                        itemsMatchers.Add(actualKV =>
                        {
                            Assert.Equal(expectedKV.Key, actualKV.Key);
                            Assert.Equal(expectedKV.Value.Version, actualKV.Value.Version);
                            Assert.Equal(expectedKV.Value.SerializedItem, actualKV.Value.SerializedItem);
                        });
                    }
                });
            }
            Assert.Collection(actual.Data, collMatchers.ToArray());
        }
    }
}
