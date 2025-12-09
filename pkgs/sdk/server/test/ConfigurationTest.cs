using System;
using System.Linq;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Hooks;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Internal;
using LaunchDarkly.Sdk.Server.Internal.DataStores;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using Xunit;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server
{
    public class ConfigurationTest
    {
        private readonly BuilderBehavior.BuildTester<ConfigurationBuilder, Configuration> _tester =
            BuilderBehavior.For(() => Configuration.Builder(sdkKey), b => b.Build())
                .WithCopyConstructor(c => Configuration.Builder(c));

        const string sdkKey = "any-key";

        [Fact]
        public void DefaultSetsKey()
        {
            var config = Configuration.Default(sdkKey);
            Assert.Equal(sdkKey, config.SdkKey);
        }

        [Fact]
        public void BuilderSetsKey()
        {
            var config = Configuration.Builder(sdkKey).Build();
            Assert.Equal(sdkKey, config.SdkKey);
        }

        [Fact]
        public void BigSegments()
        {
            var prop = _tester.Property(c => c.BigSegments, (b, v) => b.BigSegments(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(Components.BigSegments(null));
        }

        [Fact]
        public void DataSource()
        {
            var prop = _tester.Property(c => c.DataSource, (b, v) => b.DataSource(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(ComponentsImpl.NullDataSourceFactory.Instance);
        }

        [Fact]
        public void DataStore()
        {
            var prop = _tester.Property(c => c.DataStore, (b, v) => b.DataStore(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(new InMemoryDataStore().AsSingletonFactory<IDataStore>());
        }

        [Fact]
        public void DiagnosticOptOut()
        {
            var prop = _tester.Property(c => c.DiagnosticOptOut, (b, v) => b.DiagnosticOptOut(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
        }

        [Fact]
        public void Events()
        {
            var prop = _tester.Property(c => c.Events, (b, v) => b.Events(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(new MockEventProcessor().AsSingletonFactory<IEventProcessor>());
        }

        [Fact]
        public void Logging()
        {
            var prop = _tester.Property(c => c.Logging, (b, v) => b.Logging(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(Components.Logging(Logs.ToWriter(Console.Out)));
        }

        [Fact]
        public void LoggingAdapterShortcut()
        {
            var adapter = Logs.ToWriter(Console.Out);
            var config = Configuration.Builder("").Logging(adapter).Build();
            var logConfig = config.Logging.Build(new LdClientContext(""));
            Assert.Same(adapter, logConfig.LogAdapter);
        }

        [Fact]
        public void Offline()
        {
            var prop = _tester.Property(c => c.Offline, (b, v) => b.Offline(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
        }

        [Fact]
        public void SdkKey()
        {
            var prop = _tester.Property(c => c.SdkKey, (b, v) => b.SdkKey(v));
            prop.AssertCanSet("other-key");
        }

        [Fact]
        public void StartWaitTime()
        {
            var prop = _tester.Property(c => c.StartWaitTime, (b, v) => b.StartWaitTime(v));
            prop.AssertDefault(ConfigurationBuilder.DefaultStartWaitTime);
            prop.AssertCanSet(TimeSpan.FromSeconds(7));
        }

        [Fact]
        public void WrapperInfoDefaultsToNull()
        {
            var config = Configuration.Builder("").Build();
            Assert.Null(config.WrapperInfo);
        }

        [Fact]
        public void WrapperInfoCanBeSet()
        {
            var config = Configuration.Builder("")
                .WrapperInfo(Components.WrapperInfo().Name("name").Version("version")).Build();
            var wrapperInfo = config.WrapperInfo.Build();
            Assert.Equal("name", wrapperInfo.Name);
            Assert.Equal("version", wrapperInfo.Version);
        }

        [Fact]
        public void NoHooksByDefault()
        {
            var config = Configuration.Builder("").Hooks(Components.Hooks()).Build();
            var hooks = config.Hooks.Build();
            Assert.Empty(hooks.Hooks);
        }

        [Fact]
        public void CanAddArbitraryHooks()
        {
            var config = Configuration.Builder("").Hooks(
                    Components.Hooks()
                        .Add(new Hook("foo"))
                        .Add(new Hook("bar")))
                .Build();

            var hooks = config.Hooks.Build();
            Assert.Equal(2, hooks.Hooks.Count());
        }

        [Fact]
        public void CanAddArbitraryHooksFromEnumerable()
        {
            var config = Configuration.Builder("").Hooks(
                    Components.Hooks(new[] { new Hook("foo"), new Hook("bar") }))
                .Build();

            var hooks = config.Hooks.Build();
            Assert.Equal(2, hooks.Hooks.Count());
        }

        [Fact]
        public void CanConfigureDefaultDataSystem()
        {
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Default()).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Equal(1, dataSystemConfig.Initializers.Count);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Initializers[0]);
            Assert.Equal(2, dataSystemConfig.Synchronizers.Count);
            Assert.IsType<FDv2StreamingDataSourceBuilder>(dataSystemConfig.Synchronizers[0]);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Synchronizers[1]);
            Assert.IsType<PollingDataSourceBuilder>(dataSystemConfig.FDv1FallbackSynchronizer);
            Assert.Null(dataSystemConfig.PersistentStore);
        }

        [Fact]
        public void CanConfigureStreamingDataSystem()
        {
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Streaming()).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Empty(dataSystemConfig.Initializers);
            Assert.Equal(1, dataSystemConfig.Synchronizers.Count);
            Assert.IsType<FDv2StreamingDataSourceBuilder>(dataSystemConfig.Synchronizers[0]);
            Assert.IsType<PollingDataSourceBuilder>(dataSystemConfig.FDv1FallbackSynchronizer);
            Assert.Null(dataSystemConfig.PersistentStore);
        }

        [Fact]
        public void CanConfigurePollingDataSystem()
        {
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Polling()).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Empty(dataSystemConfig.Initializers);
            Assert.Equal(1, dataSystemConfig.Synchronizers.Count);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Synchronizers[0]);
            Assert.IsType<PollingDataSourceBuilder>(dataSystemConfig.FDv1FallbackSynchronizer);
            Assert.Null(dataSystemConfig.PersistentStore);
        }

        [Fact]
        public void CanConfigureDaemonDataSystem()
        {
            var mockStore =
                Components.PersistentDataStore(new MockPersistentStore().AsSingletonFactory<IPersistentDataStore>());
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Daemon(mockStore)).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Empty(dataSystemConfig.Initializers);
            Assert.Empty(dataSystemConfig.Synchronizers);
            Assert.Null(dataSystemConfig.FDv1FallbackSynchronizer);
            Assert.NotNull(dataSystemConfig.PersistentStore);
            Assert.Equal(DataSystemConfiguration.DataStoreMode.ReadOnly, dataSystemConfig.PersistentDataStoreMode);
        }

        [Fact]
        public void CanConfigurePersistentStoreDataSystem()
        {
            var mockStore =
                Components.PersistentDataStore(new MockPersistentStore().AsSingletonFactory<IPersistentDataStore>());
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().PersistentStore(mockStore)).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Equal(1, dataSystemConfig.Initializers.Count);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Initializers[0]);
            Assert.Equal(2, dataSystemConfig.Synchronizers.Count);
            Assert.IsType<FDv2StreamingDataSourceBuilder>(dataSystemConfig.Synchronizers[0]);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Synchronizers[1]);
            Assert.IsType<PollingDataSourceBuilder>(dataSystemConfig.FDv1FallbackSynchronizer);
            Assert.NotNull(dataSystemConfig.PersistentStore);
            Assert.Equal(DataSystemConfiguration.DataStoreMode.ReadWrite, dataSystemConfig.PersistentDataStoreMode);
        }

        [Fact]
        public void CanConfigureCustomDataSystemWithAllOptions()
        {
            var mockStore =
                Components.PersistentDataStore(new MockPersistentStore().AsSingletonFactory<IPersistentDataStore>());

            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Custom()
                    .Initializers(DataSystemComponents.Polling())
                    .Synchronizers(DataSystemComponents.Streaming(), DataSystemComponents.Polling())
                    .FDv1FallbackSynchronizer(DataSystemComponents.FDv1Polling())
                    .PersistentStore(mockStore, DataSystemConfiguration.DataStoreMode.ReadWrite)
                ).Build();

            var dataSystemConfig = config.DataSystem.Build();

            // Verify initializers
            Assert.Equal(1, dataSystemConfig.Initializers.Count);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Initializers[0]);

            // Verify synchronizers
            Assert.Equal(2, dataSystemConfig.Synchronizers.Count);
            Assert.IsType<FDv2StreamingDataSourceBuilder>(dataSystemConfig.Synchronizers[0]);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Synchronizers[1]);

            // Verify FDv1 fallback
            Assert.IsType<PollingDataSourceBuilder>(dataSystemConfig.FDv1FallbackSynchronizer);

            // Verify persistent store and mode
            Assert.NotNull(dataSystemConfig.PersistentStore);
            Assert.Equal(DataSystemConfiguration.DataStoreMode.ReadWrite, dataSystemConfig.PersistentDataStoreMode);
        }

        [Fact]
        public void CanReplaceInitializersInCustomDataSystem()
        {
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Custom()
                    .Initializers(DataSystemComponents.Polling())
                    .ReplaceInitializers(DataSystemComponents.Streaming())
                ).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Equal(1, dataSystemConfig.Initializers.Count);
            Assert.IsType<FDv2StreamingDataSourceBuilder>(dataSystemConfig.Initializers[0]);
        }

        [Fact]
        public void CanReplaceSynchronizersInCustomDataSystem()
        {
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(DataSystemComponents.Polling())
                    .ReplaceSynchronizers(DataSystemComponents.Streaming())
                ).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Equal(1, dataSystemConfig.Synchronizers.Count);
            Assert.IsType<FDv2StreamingDataSourceBuilder>(dataSystemConfig.Synchronizers[0]);
        }

        [Fact]
        public void CanAddMultipleInitializersToCustomDataSystem()
        {
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Custom()
                    .Initializers(DataSystemComponents.Polling())
                    .Initializers(DataSystemComponents.Streaming())
                ).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Equal(2, dataSystemConfig.Initializers.Count);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Initializers[0]);
            Assert.IsType<FDv2StreamingDataSourceBuilder>(dataSystemConfig.Initializers[1]);
        }

        [Fact]
        public void CanAddMultipleSynchronizersToCustomDataSystem()
        {
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(DataSystemComponents.Polling())
                    .Synchronizers(DataSystemComponents.Streaming())
                    .Synchronizers(DataSystemComponents.FDv1Polling())
                ).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Equal(3, dataSystemConfig.Synchronizers.Count);
            Assert.IsType<FDv2PollingDataSourceBuilder>(dataSystemConfig.Synchronizers[0]);
            Assert.IsType<FDv2StreamingDataSourceBuilder>(dataSystemConfig.Synchronizers[1]);
            Assert.IsType<PollingDataSourceBuilder>(dataSystemConfig.Synchronizers[2]);
        }

        [Fact]
        public void CustomDataSystemWithNoConfigurationHasEmptyLists()
        {
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Custom()).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.Empty(dataSystemConfig.Initializers);
            Assert.Empty(dataSystemConfig.Synchronizers);
            Assert.Null(dataSystemConfig.FDv1FallbackSynchronizer);
            Assert.Null(dataSystemConfig.PersistentStore);
        }

        [Fact]
        public void CanConfigureDaemonDataSystemWithReadOnlyMode()
        {
            var mockStore =
                Components.PersistentDataStore(new MockPersistentStore().AsSingletonFactory<IPersistentDataStore>());
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Custom()
                    .PersistentStore(mockStore, DataSystemConfiguration.DataStoreMode.ReadOnly)
                ).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.NotNull(dataSystemConfig.PersistentStore);
            Assert.Equal(DataSystemConfiguration.DataStoreMode.ReadOnly, dataSystemConfig.PersistentDataStoreMode);
            Assert.Empty(dataSystemConfig.Initializers);
            Assert.Empty(dataSystemConfig.Synchronizers);
        }

        [Fact]
        public void CanConfigureCustomDataSystemWithReadWritePersistentStore()
        {
            var mockStore =
                Components.PersistentDataStore(new MockPersistentStore().AsSingletonFactory<IPersistentDataStore>());
            var config = Configuration.Builder("")
                .DataSystem(Components.DataSystem().Custom()
                    .PersistentStore(mockStore, DataSystemConfiguration.DataStoreMode.ReadWrite)
                    .Synchronizers(DataSystemComponents.Streaming())
                ).Build();

            var dataSystemConfig = config.DataSystem.Build();
            Assert.NotNull(dataSystemConfig.PersistentStore);
            Assert.Equal(DataSystemConfiguration.DataStoreMode.ReadWrite, dataSystemConfig.PersistentDataStoreMode);
            Assert.Equal(1, dataSystemConfig.Synchronizers.Count);
        }

        // Simple mock persistent data store for testing
        private class MockPersistentStore : IPersistentDataStore
        {
            public void Init(FullDataSet<SerializedItemDescriptor> allData)
            {
            }

            public SerializedItemDescriptor? Get(DataKind kind, string key) => null;

            public KeyedItems<SerializedItemDescriptor> GetAll(DataKind kind) =>
                new KeyedItems<SerializedItemDescriptor>(null);

            public bool Upsert(DataKind kind, string key, SerializedItemDescriptor item) => true;
            public bool Initialized() => true;
            public bool IsStoreAvailable() => true;

            public void Dispose()
            {
            }
        }
    }
}
