using System;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    public class LdClientDataSourceStatusTests : BaseTest
    {
        // This is separate from LdClientListenersTest because the client-side .NET SDK has
        // more complicated connection-status behavior than the server-side one and we need
        // to test more scenarios. For basic scenarios, we can just use TestData to inject
        // status changes; for others, we need to use mock components to simulate the
        // inputs that can lead to status changes.

        public LdClientDataSourceStatusTests(ITestOutputHelper testOutput) : base(testOutput) { }

        [Fact]
        public void DataSourceStatusProviderReturnsLatestStatus()
        {
            var testData = TestData.DataSource();
            var config = BasicConfig().DataSource(testData).Build();
            var timeBeforeStarting = DateTime.Now;

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var initialStatus = client.DataSourceStatusProvider.Status;
                Assert.Equal(DataSourceState.Valid, initialStatus.State);
                Assert.True(initialStatus.StateSince >= timeBeforeStarting);
                Assert.Null(initialStatus.LastError);

                var errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(401);
                testData.UpdateStatus(DataSourceState.Shutdown, errorInfo);

                var newStatus = client.DataSourceStatusProvider.Status;
                Assert.Equal(DataSourceState.Shutdown, newStatus.State);
                Assert.True(newStatus.StateSince >= errorInfo.Time);
                Assert.Equal(errorInfo, newStatus.LastError);
            }
        }

        [Fact]
        public void DataSourceStatusProviderSendsStatusUpdates()
        {
            var testData = TestData.DataSource();
            var config = BasicConfig().DataSource(testData).Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var statuses = new EventSink<DataSourceStatus>();
                client.DataSourceStatusProvider.StatusChanged += statuses.Add;

                var errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(401);
                testData.UpdateStatus(DataSourceState.Shutdown, errorInfo);

                var newStatus = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Shutdown, newStatus.State);
                Assert.True(newStatus.StateSince >= errorInfo.Time);
                Assert.Equal(errorInfo, newStatus.LastError);
            }
        }

        [Fact]
        public void DataSourceStatusStartsAsInitializing()
        {
            var config = BasicConfig()
                .DataSource(new MockDataSourceThatNeverInitializes().AsSingletonFactory<IDataSource>())
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var initialStatus = client.DataSourceStatusProvider.Status;
                Assert.Equal(DataSourceState.Initializing, initialStatus.State);
                Assert.Null(initialStatus.LastError);
            }
        }

        [Fact]
        public void DataSourceStatusRemainsInitializingAfterErrorIfNeverInitialized()
        {
            var dataSourceFactory = new CapturingComponentConfigurer<IDataSource>(
                new MockDataSource().AsSingletonFactory<IDataSource>());

            var config = BasicConfig()
                .DataSource(dataSourceFactory)
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var statuses = new EventSink<DataSourceStatus>();
                client.DataSourceStatusProvider.StatusChanged += statuses.Add;

                var errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(503);
                dataSourceFactory.ReceivedClientContext.DataSourceUpdateSink.UpdateStatus(
                    DataSourceState.Interrupted, errorInfo);

                var newStatus1 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Initializing, newStatus1.State);
                Assert.Equal(errorInfo, newStatus1.LastError);
            }
        }

        [Fact]
        public void DataSourceStatusIsInterruptedAfterErrorIfAlreadyInitialized()
        {
            var dataSourceFactory = new CapturingComponentConfigurer<IDataSource>(
                new MockDataSource().AsSingletonFactory<IDataSource>());

            var config = BasicConfig()
                .DataSource(dataSourceFactory)
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var statuses = new EventSink<DataSourceStatus>();
                client.DataSourceStatusProvider.StatusChanged += statuses.Add;

                dataSourceFactory.ReceivedClientContext.DataSourceUpdateSink.UpdateStatus(
                    DataSourceState.Valid, null);

                var newStatus1 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Valid, newStatus1.State);

                var errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(503);
                dataSourceFactory.ReceivedClientContext.DataSourceUpdateSink.UpdateStatus(
                    DataSourceState.Interrupted, errorInfo);

                var newStatus2 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Interrupted, newStatus2.State);
                Assert.Equal(errorInfo, newStatus2.LastError);
            }
        }

        [Fact]
        public void DataSourceStatusStartsAsSetOfflineIfConfiguredOffline()
        {
            var config = BasicConfig().Offline(true).Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var initialStatus = client.DataSourceStatusProvider.Status;
                Assert.Equal(DataSourceState.SetOffline, initialStatus.State);
                Assert.Null(initialStatus.LastError);
            }
        }

        [Fact]
        public void DataSourceStatusIsRestoredWhenNoLongerSetOffline()
        {
            var testData = TestData.DataSource();
            var config = BasicConfig().DataSource(testData).Offline(true).Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(client.DataSourceStatusProvider.WaitFor(DataSourceState.SetOffline, TimeSpan.FromSeconds(5)));

                var statuses = new EventSink<DataSourceStatus>();
                client.DataSourceStatusProvider.StatusChanged += statuses.Add;

                client.SetOffline(false, TimeSpan.FromSeconds(1));

                var newStatus1 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Initializing, newStatus1.State);

                var newStatus2 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Valid, newStatus2.State);
            }
        }

        [Fact]
        public void DataSourceStatusStartsAsNetworkUnavailableIfNetworkIsUnavailable()
        {
            var connectivity = new MockConnectivityStateManager(false);
            var config = BasicConfig().ConnectivityStateManager(connectivity).Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var initialStatus = client.DataSourceStatusProvider.Status;
                Assert.Equal(DataSourceState.NetworkUnavailable, initialStatus.State);
                Assert.Null(initialStatus.LastError);
            }
        }

        [Fact]
        public void DataSourceStatusIsRestoredWhenNetworkIsAvailableAgain()
        {
            var testData = TestData.DataSource();
            var connectivity = new MockConnectivityStateManager(false);
            var config = BasicConfig()
                .DataSource(testData)
                .ConnectivityStateManager(connectivity)
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(client.DataSourceStatusProvider.WaitFor(DataSourceState.NetworkUnavailable, TimeSpan.FromSeconds(5)));

                var statuses = new EventSink<DataSourceStatus>();
                client.DataSourceStatusProvider.StatusChanged += statuses.Add;

                connectivity.Connect(true);

                var newStatus1 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Initializing, newStatus1.State);

                var newStatus2 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Valid, newStatus2.State);
            }
        }

        [Fact]
        public void SetOfflineStatusOverridesNetworkUnavailableStatus()
        {
            var testData = TestData.DataSource();
            var connectivity = new MockConnectivityStateManager(false);
            var config = BasicConfig()
                .DataSource(testData)
                .ConnectivityStateManager(connectivity)
                .Offline(true)
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(client.DataSourceStatusProvider.WaitFor(DataSourceState.SetOffline, TimeSpan.FromSeconds(5)));

                var statuses = new EventSink<DataSourceStatus>();
                client.DataSourceStatusProvider.StatusChanged += statuses.Add;

                client.SetOffline(false, TimeSpan.FromSeconds(1));

                var newStatus1 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.NetworkUnavailable, newStatus1.State);

                connectivity.Connect(true);

                var newStatus2 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Initializing, newStatus2.State);

                var newStatus3 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Valid, newStatus3.State);
            }
        }

        [Fact]
        public void BackgroundDisabledState()
        {
            var testData = TestData.DataSource();
            var backgrounder = new MockBackgroundModeManager();
            var config = BasicConfig()
                .BackgroundModeManager(backgrounder)
                .DataSource(testData)
                .EnableBackgroundUpdating(false)
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(client.DataSourceStatusProvider.WaitFor(DataSourceState.Valid, TimeSpan.FromSeconds(5)));

                var statuses = new EventSink<DataSourceStatus>();
                client.DataSourceStatusProvider.StatusChanged += statuses.Add;

                backgrounder.UpdateBackgroundMode(true);

                var newStatus1 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.BackgroundDisabled, newStatus1.State);

                backgrounder.UpdateBackgroundMode(false);

                var newStatus2 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Initializing, newStatus2.State);

                var newStatus3 = statuses.ExpectValue();
                Assert.Equal(DataSourceState.Valid, newStatus3.State);
            }
        }
    }
}
