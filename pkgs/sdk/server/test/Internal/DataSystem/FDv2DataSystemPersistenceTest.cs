using System;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataStores;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using LaunchDarkly.TestHelpers.HttpTest;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.MockResponses;

namespace LaunchDarkly.Sdk.Server.Internal.DataSystem
{
    /// <summary>
    /// Verifies which data the FDv2 data system writes to a persistent store in read-write mode: each data
    /// source decides whether its data is persisted.
    /// </summary>
    public class FDv2DataSystemPersistenceTest : BaseTest
    {
        private static readonly TimeSpan WaitTime = TimeSpan.FromSeconds(5);

        private readonly MockCoreSync _store = new MockCoreSync();
        private readonly Context _context = Context.New("user-key");

        public FDv2DataSystemPersistenceTest(ITestOutputHelper testOutput) : base(testOutput) { }

        private DataSystemBuilder DataSystemWithPersistentStore() =>
            Components.DataSystem().Custom().PersistentStore(
                Components.PersistentDataStore(_store.AsSingletonFactory<IPersistentDataStore>()),
                DataSystemConfiguration.DataStoreMode.ReadWrite);

        private LdClient MakeClient(DataSystemBuilder dataSystem) =>
            new LdClient(BasicConfig().DataSystem(dataSystem).StartWaitTime(WaitTime).Build());

        private bool StoreHasFlag(string key) =>
            _store.Data.TryGetValue(DataModel.Features, out var flags) && flags.ContainsKey(key);

        private static void WaitUntil(Func<bool> condition, string description)
        {
            var deadline = DateTime.Now + WaitTime;
            while (!condition())
            {
                if (DateTime.Now > deadline)
                {
                    Assert.True(false, "timed out waiting for " + description);
                }
                Thread.Sleep(10);
            }
        }

        [Fact]
        public void FileDataInitializerIsNotWrittenToPersistentStore()
        {
            using (var file = TempFile.Create())
            {
                file.SetContent(@"{""flagValues"":{""flag1"":""a""}}");

                using (var client = MakeClient(DataSystemWithPersistentStore()
                           .Initializers(FileData.DataSource().FilePaths(file.Path))))
                {
                    Assert.Equal("a", client.StringVariation("flag1", _context, ""));
                    Assert.Equal(0, _store.InitCalledCount);
                    Assert.Empty(_store.Data);
                }
            }
        }

        [Fact]
        public void FileDataSynchronizerReloadIsNotWrittenToPersistentStore()
        {
            using (var file = TempFile.Create())
            {
                file.SetContent(@"{""flagValues"":{""flag1"":""a""}}");

                using (var client = MakeClient(DataSystemWithPersistentStore()
                           .Synchronizers(FileData.DataSource().FilePaths(file.Path).AutoUpdate(true))))
                {
                    Assert.Equal("a", client.StringVariation("flag1", _context, ""));
                    var events = new EventSink<FlagChangeEvent>();
                    client.FlagTracker.FlagChanged += events.Add;

                    file.SetContent(@"{""flagValues"":{""flag1"":""b""}}");

                    Assert.Equal("flag1", events.ExpectValue(WaitTime).Key);
                    Assert.Equal("b", client.StringVariation("flag1", _context, ""));
                    Assert.Equal(0, _store.InitCalledCount);
                    Assert.Empty(_store.Data);
                }
            }
        }

        [Fact]
        public void FileDataIsWrittenToPersistentStoreWhenRequested()
        {
            using (var file = TempFile.Create())
            {
                file.SetContent(@"{""flagValues"":{""flag1"":""a""}}");

                using (var client = MakeClient(DataSystemWithPersistentStore()
                           .Initializers(FileData.DataSource().FilePaths(file.Path).ShouldPersist(true))))
                {
                    Assert.True(StoreHasFlag("flag1"));
                }
            }
        }

        [Fact]
        public void TestDataIsWrittenToPersistentStoreByDefault()
        {
            var td = TestData.DataSource();
            td.Update(td.Flag("flag1").On(true));

            using (var client = MakeClient(DataSystemWithPersistentStore().Synchronizers(td)))
            {
                Assert.True(StoreHasFlag("flag1"));

                td.Update(td.Flag("flag2").On(true));

                Assert.True(StoreHasFlag("flag2"));
            }
        }

        [Fact]
        public void TestDataIsNotWrittenToPersistentStoreWhenNotRequested()
        {
            var td = TestData.DataSource().ShouldPersist(false);
            td.Update(td.Flag("flag1").On(true));

            using (var client = MakeClient(DataSystemWithPersistentStore().Synchronizers(td)))
            {
                Assert.True(client.BoolVariation("flag1", _context, false));

                td.Update(td.Flag("flag2").On(true));

                Assert.True(client.BoolVariation("flag2", _context, false));
                Assert.Equal(0, _store.InitCalledCount);
                Assert.Empty(_store.Data);
            }
        }

        [Fact]
        public void CustomDataSourceIsWrittenToPersistentStore()
        {
            var data = new DataSetBuilder().Flags(new FeatureFlagBuilder("flag1").Version(1).Build()).Build();

            using (var client = MakeClient(DataSystemWithPersistentStore()
                       .Synchronizers(new CustomDataSource(data))))
            {
                Assert.True(StoreHasFlag("flag1"));
            }
        }

        [Fact]
        public void FDv1FallbackSynchronizerDataIsWrittenToPersistentStore()
        {
            var data = new DataSetBuilder().Flags(new FeatureFlagBuilder("flag1").Version(1).Build()).Build();

            using (var pollServer = HttpServer.Start(PollingResponse(data)))
            {
                var config = BasicConfig()
                    .DataSystem(DataSystemWithPersistentStore()
                        .Synchronizers(new FDv1FallbackDirectiveSource())
                        .FDv1FallbackSynchronizer(DataSystemComponents.FDv1Polling()))
                    .ServiceEndpoints(Components.ServiceEndpoints().Polling(pollServer.Uri))
                    .StartWaitTime(WaitTime)
                    .Build();

                using (var client = new LdClient(config))
                {
                    WaitUntil(() => StoreHasFlag("flag1"), "the FDv1 fallback data in the persistent store");
                }
            }
        }

        [Fact]
        public void PersistedSynchronizerBasisAfterFileDataInitializerIsWrittenToPersistentStore()
        {
            var td = TestData.DataSource();
            td.Update(td.Flag("td-flag").On(true));

            using (var file = TempFile.Create())
            {
                file.SetContent(@"{""flagValues"":{""file-flag"":""a""}}");

                using (var client = MakeClient(DataSystemWithPersistentStore()
                           .Initializers(FileData.DataSource().FilePaths(file.Path))
                           .Synchronizers(td)))
                {
                    WaitUntil(() => StoreHasFlag("td-flag"), "the synchronizer data in the persistent store");
                    Assert.False(StoreHasFlag("file-flag"));
                }
            }
        }

        [Fact]
        public void FileDataSourceInFDv1IsStillWrittenToPersistentStore()
        {
            using (var file = TempFile.Create())
            {
                file.SetContent(@"{""flagValues"":{""flag1"":""a""}}");

                var config = BasicConfig()
                    .DataSource(FileData.DataSource().FilePaths(file.Path))
                    .DataStore(Components.PersistentDataStore(_store.AsSingletonFactory<IPersistentDataStore>()))
                    .StartWaitTime(WaitTime)
                    .Build();

                using (var client = new LdClient(config))
                {
                    Assert.True(StoreHasFlag("flag1"));
                }
            }
        }

        // An FDv1-style data source that is not one of the SDK's own.
        private sealed class CustomDataSource : IComponentConfigurer<IDataSource>, IDataSource
        {
            private readonly DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> _data;
            private IDataSourceUpdates _updates;

            public CustomDataSource(DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> data)
            {
                _data = data;
            }

            public IDataSource Build(LdClientContext context)
            {
                _updates = context.DataSourceUpdates;
                return this;
            }

            public Task<bool> Start()
            {
                _updates.Init(_data);
                return Task.FromResult(true);
            }

            public bool Initialized => true;

            public void Dispose() { }
        }

        // A synchronizer that reports the FDv1 fallback directive as soon as it starts, as an FDv2 source
        // does when LaunchDarkly tells the SDK to fall back.
        private sealed class FDv1FallbackDirectiveSource : IComponentConfigurer<IDataSource>, IDataSource
        {
            private IDataSourceUpdates _updates;

            public IDataSource Build(LdClientContext context)
            {
                _updates = context.DataSourceUpdates;
                return this;
            }

            public Task<bool> Start()
            {
                _updates.UpdateStatus(DataSourceState.Interrupted, new DataSourceStatus.ErrorInfo
                {
                    Kind = DataSourceStatus.ErrorKind.ErrorResponse,
                    StatusCode = 503,
                    FDv1Fallback = true,
                    Time = DateTime.Now
                });
                return new TaskCompletionSource<bool>().Task;
            }

            public bool Initialized => false;

            public void Dispose() { }
        }
    }
}
