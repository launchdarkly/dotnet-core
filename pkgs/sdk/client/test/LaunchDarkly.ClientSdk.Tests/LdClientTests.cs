using System;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Subsystems;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    public class LdClientTests : BaseTest
    {
        private static readonly Context AnonUser = Context.Builder("anon-placeholder-key")
            .Anonymous(true)
            .Set("email", "example")
            .Set("other", 3)
            .Build();

        public LdClientTests(ITestOutputHelper testOutput) : base(testOutput) { }

        [Fact]
        public void CannotCreateClientWithNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => LdClient.Init((Configuration)null, BasicUser, TimeSpan.Zero));
        }

        [Fact]
        public void CannotCreateClientWithNegativeWaitTime()
        {
            var config = BasicConfig().Build();
            Assert.Throws<ArgumentOutOfRangeException>(() => LdClient.Init(config, BasicUser, TimeSpan.FromMilliseconds(-2)));
        }

        [Fact]
        public void CanCreateClientWithInfiniteWaitTime()
        {
            var config = BasicConfig().Build();
            using (var client = LdClient.Init(config, BasicUser, System.Threading.Timeout.InfiniteTimeSpan)) { }
            TestUtil.ClearClient();
        }

        [Fact]
        public async void InitPassesUserToDataSource()
        {
            MockPollingProcessor stub = new MockPollingProcessor(DataSetBuilder.Empty);

            var dataSourceConfig = new CapturingComponentConfigurer<IDataSource>(stub.AsSingletonFactory<IDataSource>());
            var config = BasicConfig()
                .DataSource(dataSourceConfig)
                .Build();

            using (var client = await LdClient.InitAsync(config, BasicUser))
            {
                var actualUser = client.Context; // may have been transformed e.g. to add device/OS properties
                Assert.Equal(BasicUser.Key, actualUser.Key);
                Assert.Equal(actualUser, dataSourceConfig.ReceivedClientContext.CurrentContext);
            }
        }

        [Fact]
        public async Task InitWithAnonUserAddsRandomizedKey()
        {
            // Note, we don't care about polling mode vs. streaming mode for this functionality.
            var config = BasicConfig().Persistence(Components.NoPersistence).GenerateAnonymousKeys(true).Build();

            string key1;

            using (var client = await TestUtil.CreateClientAsync(config, AnonUser))
            {
                key1 = client.Context.FullyQualifiedKey;
                Assert.NotNull(key1);
                Assert.NotEqual("", key1);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(key1).Build(),
                    client.Context);
            }

            // Starting again should generate a new key, since we've turned off persistence
            using (var client = await TestUtil.CreateClientAsync(config, AnonUser))
            {
                var key2 = client.Context.FullyQualifiedKey;
                Assert.NotNull(key2);
                Assert.NotEqual("", key2);
                Assert.NotEqual(key1, key2);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(key2).Build(),
                    client.Context);
            }
        }

        [Fact]
        public async Task InitWithAnonUserDoesNotChangeKeyIfConfigOptionIsNotSet()
        {
            // Note, we don't care about polling mode vs. streaming mode for this functionality.
            var config = BasicConfig().Persistence(Components.NoPersistence).Build();

            using (var client = await TestUtil.CreateClientAsync(config, AnonUser))
            {
                AssertHelpers.ContextsEqual(AnonUser, client.Context);
            }
        }

        [Fact]
        public async Task InitWithAnonUserCanReusePreviousRandomizedKey()
        {
            // Note, we don't care about polling mode vs. streaming mode for this functionality.
            var store = new MockPersistentDataStore();
            var config = BasicConfig().Persistence(Components.Persistence().Storage(
                store.AsSingletonFactory<IPersistentDataStore>()))
                .GenerateAnonymousKeys(true).Build();

            string key1;

            using (var client = await TestUtil.CreateClientAsync(config, AnonUser))
            {
                key1 = client.Context.FullyQualifiedKey;
                Assert.NotNull(key1);
                Assert.NotEqual("", key1);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(key1).Build(),
                    client.Context);
            }

            // Starting again should reuse the persisted key
            using (var client = await TestUtil.CreateClientAsync(config, AnonUser))
            {
                Assert.Equal(key1, client.Context.FullyQualifiedKey);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(key1).Build(),
                    client.Context);
            }
        }

        [Fact]
        public async void InitWithAnonUserPassesGeneratedUserToDataSource()
        {
            MockPollingProcessor stub = new MockPollingProcessor(DataSetBuilder.Empty);

            var dataSourceConfig = new CapturingComponentConfigurer<IDataSource>(stub.AsSingletonFactory<IDataSource>());
            var config = BasicConfig()
                .DataSource(dataSourceConfig)
                .GenerateAnonymousKeys(true)
                .Build();

            using (var client = await LdClient.InitAsync(config, AnonUser))
            {
                var receivedContext = dataSourceConfig.ReceivedClientContext.CurrentContext;
                Assert.NotEqual(AnonUser, receivedContext);
                Assert.Equal(client.Context, receivedContext);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(receivedContext.FullyQualifiedKey).Build(),
                    receivedContext);
            }
        }

        [Fact]
        public async void InitWithAutoEnvAttributesEnabledAddAppInfoContext()
        {
            MockPollingProcessor stub = new MockPollingProcessor(DataSetBuilder.Empty);
            var dataSourceConfig = new CapturingComponentConfigurer<IDataSource>(stub.AsSingletonFactory<IDataSource>());
            var config = BasicConfig()
                .AutoEnvironmentAttributes(ConfigurationBuilder.AutoEnvAttributes.Enabled)
                .DataSource(dataSourceConfig)
                .GenerateAnonymousKeys(true)
                .Build();

            using (var client = await LdClient.InitAsync(config, AnonUser))
            {
                var receivedContext = dataSourceConfig.ReceivedClientContext.CurrentContext;
                Assert.True(receivedContext.TryGetContextByKind(ContextKind.Of("ld_application"), out _));
            }
        }

        [Fact]
        public async void InitWithAutoEnvAttributesDisabledNoAddedContexts()
        {
            MockPollingProcessor stub = new MockPollingProcessor(DataSetBuilder.Empty);
            var dataSourceConfig = new CapturingComponentConfigurer<IDataSource>(stub.AsSingletonFactory<IDataSource>());
            var config = BasicConfig()
                .AutoEnvironmentAttributes(ConfigurationBuilder.AutoEnvAttributes.Disabled)
                .DataSource(dataSourceConfig)
                .GenerateAnonymousKeys(true)
                .Build();

            using (var client = await LdClient.InitAsync(config, AnonUser))
            {
                var receivedContext = dataSourceConfig.ReceivedClientContext.CurrentContext;
                Assert.NotEqual(AnonUser, receivedContext);
                Assert.Equal(client.Context, receivedContext);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(receivedContext.FullyQualifiedKey).Build(),
                    receivedContext);
            }
        }

        [Fact]
        public void IdentifyUpdatesTheUser()
        {
            using (var client = TestUtil.CreateClient(BasicConfig().Build(), BasicUser))
            {
                var updatedUser = Context.New("some new key");
                var success = client.Identify(updatedUser, TimeSpan.FromSeconds(1));
                Assert.True(success);
                Assert.Equal(client.Context.FullyQualifiedKey, updatedUser.FullyQualifiedKey); // don't compare entire user, because SDK may have added device/os attributes
            }
        }

        [Fact]
        public Task IdentifyAsyncCompletesOnlyWhenNewFlagsAreAvailable()
            => IdentifyCompletesOnlyWhenNewFlagsAreAvailable((client, context) => client.IdentifyAsync(context));

        [Fact]
        public Task IdentifySyncCompletesOnlyWhenNewFlagsAreAvailable()
            => IdentifyCompletesOnlyWhenNewFlagsAreAvailable((client, context) => Task.Run(() => client.Identify(context, TimeSpan.FromSeconds(4))));

        private async Task IdentifyCompletesOnlyWhenNewFlagsAreAvailable(Func<LdClient, Context, Task> identifyTask)
        {
            var userA = Context.New("a");
            var userB = Context.New("b");

            var flagKey = "flag";
            var userAFlags = new DataSetBuilder()
                .Add(flagKey, 1, LdValue.Of("a-value"), 0).Build();
            var userBFlags = new DataSetBuilder()
                .Add(flagKey, 2, LdValue.Of("b-value"), 1).Build();

            var startedIdentifyUserB = new SemaphoreSlim(0, 1);
            var canFinishIdentifyUserB = new SemaphoreSlim(0, 1);
            var finishedIdentifyUserB = new SemaphoreSlim(0, 1);

            var dataSourceFactory = MockComponents.ComponentConfigurerFromLambda<IDataSource>(ctx =>
                new MockDataSourceFromLambda(ctx.CurrentContext, async () =>
                {
                    switch (ctx.CurrentContext.FullyQualifiedKey)
                    {
                        case "a":
                            ctx.DataSourceUpdateSink.Init(ctx.CurrentContext, userAFlags);
                            break;

                        case "b":
                            startedIdentifyUserB.Release();
                            await canFinishIdentifyUserB.WaitAsync();
                            ctx.DataSourceUpdateSink.Init(ctx.CurrentContext, userBFlags);
                            break;
                    }
                }));

            var config = BasicConfig()
                .DataSource(dataSourceFactory)
                .Build();

            using (var client = await LdClient.InitAsync(config, userA))
            {
                Assert.True(client.Initialized);
                Assert.Equal("a-value", client.StringVariation(flagKey, null));

                var identifyUserBTask = Task.Run(async () =>
                {
                    await identifyTask(client, userB);
                    finishedIdentifyUserB.Release();
                });

                await startedIdentifyUserB.WaitAsync();

                Assert.False(client.Initialized);
                Assert.Null(client.StringVariation(flagKey, null));

                canFinishIdentifyUserB.Release();
                await finishedIdentifyUserB.WaitAsync();

                Assert.True(client.Initialized);
                Assert.Equal("b-value", client.StringVariation(flagKey, null));
            }
        }

        [Fact]
        public async void IdentifyPassesUserToDataSource()
        {
            MockPollingProcessor stub = new MockPollingProcessor(DataSetBuilder.Empty);
            Context newUser = Context.New("new-user");

            var dataSourceConfig = new CapturingComponentConfigurer<IDataSource>(stub.AsSingletonFactory<IDataSource>());
            var config = BasicConfig()
                .DataSource(dataSourceConfig)
                .Build();

            using (var client = await LdClient.InitAsync(config, BasicUser))
            {
                var receivedContext = dataSourceConfig.ReceivedClientContext.CurrentContext;
                AssertHelpers.ContextsEqual(BasicUser, client.Context);
                Assert.Equal(client.Context, receivedContext);

                await client.IdentifyAsync(newUser);

                receivedContext = dataSourceConfig.ReceivedClientContext.CurrentContext;
                AssertHelpers.ContextsEqual(newUser, client.Context);
                Assert.Equal(client.Context, receivedContext);
            }
        }

        [Fact]
        public async Task IdentifyWithAnonUserAddsRandomizedKey()
        {
            var config = BasicConfig().Persistence(Components.NoPersistence).GenerateAnonymousKeys(true).Build();

            string key1;

            using (var client = await TestUtil.CreateClientAsync(config, BasicUser))
            {
                await client.IdentifyAsync(AnonUser);

                key1 = client.Context.FullyQualifiedKey;
                Assert.NotNull(key1);
                Assert.NotEqual("", key1);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(key1).Build(),
                    client.Context);

                var anonUser2 = TestUtil.BuildAutoContext().Name("other").Build();
                await client.IdentifyAsync(anonUser2);
                var key2 = client.Context.FullyQualifiedKey;
                Assert.Equal(key1, key2); // Even though persistence is disabled, the key is stable during the lifetime of the SDK client.
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(anonUser2).Key(key2).Build(),
                    client.Context);
            }

            using (var client = await TestUtil.CreateClientAsync(config, BasicUser))
            {
                await client.IdentifyAsync(AnonUser);

                var key3 = client.Context.FullyQualifiedKey;
                Assert.NotNull(key3);
                Assert.NotEqual("", key3);
                Assert.NotEqual(key1, key3); // The previously generated key was discarded with the previous client.
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(key3).Build(),
                    client.Context);
            }
        }

        [Fact]
        public async Task IdentifyWithAnonUserDoesNotChangeKeyIfConfigOptionIsNotSet()
        {
            var config = BasicConfig().Persistence(Components.NoPersistence).Build();

            using (var client = await TestUtil.CreateClientAsync(config, BasicUser))
            {
                await client.IdentifyAsync(AnonUser);

                AssertHelpers.ContextsEqual(AnonUser, client.Context);
            }
        }

        [Fact]
        public async Task IdentifyWithAnonUserCanReusePersistedRandomizedKey()
        {
            var store = new MockPersistentDataStore();
            var config = BasicConfig().Persistence(Components.Persistence().Storage(
                store.AsSingletonFactory<IPersistentDataStore>()))
                .GenerateAnonymousKeys(true).Build();

            string key1;

            using (var client = await TestUtil.CreateClientAsync(config, BasicUser))
            {
                await client.IdentifyAsync(AnonUser);

                key1 = client.Context.FullyQualifiedKey;
                Assert.NotNull(key1);
                Assert.NotEqual("", key1);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(key1).Build(),
                    client.Context);
            }

            using (var client = await TestUtil.CreateClientAsync(config, BasicUser))
            {
                await client.IdentifyAsync(AnonUser);

                var key2 = client.Context.FullyQualifiedKey;
                Assert.Equal(key1, key2);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(key2).Build(),
                    client.Context);
            }
        }

        [Fact]
        public async void IdentifyWithAnonUserPassesGeneratedUserToDataSource()
        {
            MockPollingProcessor stub = new MockPollingProcessor(DataSetBuilder.Empty);

            var dataSourceConfig = new CapturingComponentConfigurer<IDataSource>(stub.AsSingletonFactory<IDataSource>());
            var config = BasicConfig()
                .DataSource(dataSourceConfig)
                .GenerateAnonymousKeys(true)
                .Build();

            using (var client = await LdClient.InitAsync(config, BasicUser))
            {
                await client.IdentifyAsync(AnonUser);

                var receivedContext = dataSourceConfig.ReceivedClientContext.CurrentContext;
                Assert.NotEqual(AnonUser, receivedContext);
                Assert.Equal(client.Context, receivedContext);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(client.Context.FullyQualifiedKey).Build(),
                    receivedContext);
            }
        }

        [Fact]
        public async void IdentifyWithAutoEnvAttributesEnabledAddsAppInfoContext()
        {
            var stub = new MockPollingProcessor(DataSetBuilder.Empty);
            var dataSourceConfig = new CapturingComponentConfigurer<IDataSource>(stub.AsSingletonFactory<IDataSource>());
            var config = BasicConfig()
                .AutoEnvironmentAttributes(ConfigurationBuilder.AutoEnvAttributes.Enabled)
                .DataSource(dataSourceConfig)
                .GenerateAnonymousKeys(true)
                .Build();

            using (var client = await LdClient.InitAsync(config, BasicUser))
            {
                await client.IdentifyAsync(AnonUser);

                var receivedContext = dataSourceConfig.ReceivedClientContext.CurrentContext;
                Assert.True(receivedContext.TryGetContextByKind(ContextKind.Of("ld_application"), out _));
            }
        }

        [Fact]
        public async void IdentifyWithAutoEnvAttributesDisabledNoAddedContexts()
        {
            var stub = new MockPollingProcessor(DataSetBuilder.Empty);
            var dataSourceConfig = new CapturingComponentConfigurer<IDataSource>(stub.AsSingletonFactory<IDataSource>());
            var config = BasicConfig()
                .AutoEnvironmentAttributes(ConfigurationBuilder.AutoEnvAttributes.Disabled)
                .DataSource(dataSourceConfig)
                .GenerateAnonymousKeys(true)
                .Build();

            using (var client = await LdClient.InitAsync(config, BasicUser))
            {
                await client.IdentifyAsync(AnonUser);

                var receivedContext = dataSourceConfig.ReceivedClientContext.CurrentContext;
                Assert.NotEqual(AnonUser, receivedContext);
                Assert.Equal(client.Context, receivedContext);
                AssertHelpers.ContextsEqual(
                    Context.BuilderFromContext(AnonUser).Key(receivedContext.FullyQualifiedKey).Build(),
                    receivedContext);
            }
        }

        [Fact]
        public void SharedClientIsTheOnlyClientAvailable()
        {
            TestUtil.WithClientLock(() =>
            {
                var config = BasicConfig().Build();
                using (var client = LdClient.Init(config, BasicUser, TimeSpan.Zero))
                {
                    Assert.Throws<Exception>(() => LdClient.Init(config, BasicUser, TimeSpan.Zero));
                }
                TestUtil.ClearClient();
            });
        }

        [Fact]
        public void CanCreateNewClientAfterDisposingOfSharedInstance()
        {
            TestUtil.WithClientLock(() =>
            {
                TestUtil.ClearClient();
                var config = BasicConfig().Build();
                using (var client0 = LdClient.Init(config, BasicUser, TimeSpan.Zero)) { }
                Assert.Null(LdClient.Instance);
                // Dispose() is called automatically at end of "using" block
                using (var client1 = LdClient.Init(config, BasicUser, TimeSpan.Zero)) { }
            });
        }

        [Fact]
        public void ConnectionChangeShouldStopDataSource()
        {
            var mockUpdateProc = new MockPollingProcessor(null);
            var mockConnectivityStateManager = new MockConnectivityStateManager(true);
            var config = BasicConfig()
                .DataSource(mockUpdateProc.AsSingletonFactory<IDataSource>())
                .ConnectivityStateManager(mockConnectivityStateManager)
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                mockConnectivityStateManager.Connect(false);
                Assert.False(mockUpdateProc.IsRunning);
            }
        }

        [Fact]
        public void FlagsAreLoadedFromPersistentStorageByDefault()
        {
            var storage = new MockPersistentDataStore();
            var data = new DataSetBuilder().Add("flag", 1, LdValue.Of(100), 0).Build();
            var config = BasicConfig()
                .Persistence(Components.Persistence().Storage(storage.AsSingletonFactory<IPersistentDataStore>()))
                .Offline(true)
                .Build();
            storage.SetupUserData(config.MobileKey, BasicUser.Key, data);

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.Equal(100, client.IntVariation("flag", 99));
            }
        }

        [Fact]
        public void FlagsAreSavedToPersistentStorageByDefault()
        {
            var storage = new MockPersistentDataStore();
            var initialFlags = new DataSetBuilder().Add("flag", 1, LdValue.Of(100), 0).Build();
            var config = BasicConfig()
                .DataSource(MockPollingProcessor.Factory(initialFlags))
                .Persistence(Components.Persistence().Storage(storage.AsSingletonFactory<IPersistentDataStore>()))
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var storedData = storage.InspectUserData(config.MobileKey, BasicUser.Key);
                Assert.NotNull(storedData);
                AssertHelpers.DataSetsEqual(initialFlags, storedData.Value);
            }
        }

        [Fact]
        public void EventProcessorIsOnlineByDefault()
        {
            var eventProcessor = new MockEventProcessor();
            var config = BasicConfig()
                .Events(eventProcessor.AsSingletonFactory<IEventProcessor>())
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.False(eventProcessor.Offline);
            }
        }

        [Fact]
        public void EventProcessorIsOfflineWhenClientIsConfiguredOffline()
        {
            var connectivityStateManager = new MockConnectivityStateManager(true);
            var eventProcessor = new MockEventProcessor();
            var config = BasicConfig()
                .ConnectivityStateManager(connectivityStateManager)
                .Events(eventProcessor.AsSingletonFactory<IEventProcessor>())
                .Offline(true)
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(eventProcessor.Offline);

                client.SetOffline(false, TimeSpan.FromSeconds(1));
                Assert.False(eventProcessor.Offline);

                client.SetOffline(true, TimeSpan.FromSeconds(1));
                Assert.True(eventProcessor.Offline);

                // If the network is unavailable...
                connectivityStateManager.Connect(false);

                // ...then even if Offline is set to false, events stay off
                client.SetOffline(false, TimeSpan.FromSeconds(1));
                Assert.True(eventProcessor.Offline);
            }
        }

        [Fact]
        public void EventProcessorIsOfflineWhenNetworkIsUnavailable()
        {
            var connectivityStateManager = new MockConnectivityStateManager(false);
            var eventProcessor = new MockEventProcessor();
            var config = BasicConfig()
                .ConnectivityStateManager(connectivityStateManager)
                .Events(eventProcessor.AsSingletonFactory<IEventProcessor>())
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(eventProcessor.Offline);

                connectivityStateManager.Connect(true);
                Assert.False(eventProcessor.Offline);

                connectivityStateManager.Connect(false);
                Assert.True(eventProcessor.Offline);

                // If client is configured offline...
                client.SetOffline(true, TimeSpan.FromSeconds(1));

                // ...then even if the network comes back on, events stay off
                connectivityStateManager.Connect(true);
                Assert.True(eventProcessor.Offline);
            }
        }
    }
}
