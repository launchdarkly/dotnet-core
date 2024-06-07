using System;
using System.Net;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.TestHelpers;
using LaunchDarkly.TestHelpers.HttpTest;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Client.MockResponses;
using static LaunchDarkly.Sdk.Client.TestHttpUtils;
using static LaunchDarkly.TestHelpers.JsonAssertions;
using static LaunchDarkly.TestHelpers.JsonTestValue;

namespace LaunchDarkly.Sdk.Client
{
    public class LdClientDiagnosticEventTest : BaseTest
    {
        // These tests cover the basic functionality of sending diagnostic events, and
        // also verify that properties set with Configuration.Builder show up correctly
        // in the configuration part of the diagnostic data. The lower-level details of
        // how diagnostic events are accumulated in memory and delivered are tested in
        // LaunchDarkly.InternalSdk, and the details of how stream connection data is
        // logged in diagnostic events is tested in StreamProcessorTest.

        internal static readonly TimeSpan testStartWaitTime = TimeSpan.FromMilliseconds(1);

        private MockEventSender _testEventSender = new MockEventSender { FilterKind = EventDataKind.DiagnosticEvent };

        public LdClientDiagnosticEventTest(ITestOutputHelper testOutput) : base(testOutput) { }

        [Fact]
        public void NoDiagnosticInitEventIsSentIfOptedOut()
        {
            var config = BasicConfig()
                .DiagnosticOptOut(true)
                .Events(Components.SendEvents().EventSender(_testEventSender))
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                _testEventSender.RequireNoPayloadSent(TimeSpan.FromMilliseconds(100));
            }
        }

        [Fact]
        public void DiagnosticInitEventIsSent()
        {
            var testWrapperName = "wrapper-name";
            var testWrapperVersion = "1.2.3";
            var expectedSdk = JsonOf(LdValue.BuildObject()
                .Add("name", "dotnet-client-sdk")
                .Add("version", AssemblyVersions.GetAssemblyVersionStringForType(typeof(LdClient)))
                .Add("wrapperName", testWrapperName)
                .Add("wrapperVersion", testWrapperVersion)
                .Build().ToJsonString());

            var config = BasicConfig()
                .Events(Components.SendEvents().EventSender(_testEventSender))
                .Http(Components.HttpConfiguration().Wrapper(testWrapperName, testWrapperVersion))
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var payload = _testEventSender.RequirePayload();

                Assert.Equal(EventDataKind.DiagnosticEvent, payload.Kind);
                Assert.Equal(1, payload.EventCount);

                var data = JsonOf(payload.Data);
                AssertJsonEqual(JsonFromValue("diagnostic-init"), data.Property("kind"));
                AssertJsonEqual(expectedSdk, data.Property("sdk"));
                AssertJsonEqual(JsonFromValue(BasicMobileKey.Substring(BasicMobileKey.Length - 6)),
                    data.Property("id").Property("sdkKeySuffix"));

                data.RequiredProperty("creationDate");
            }
        }

        [Fact]
        public void DiagnosticPeriodicEventsAreSent()
        {
            var config = BasicConfig()
                .Events(Components.SendEvents()
                    .EventSender(_testEventSender)
                    .DiagnosticRecordingIntervalNoMinimum(TimeSpan.FromMilliseconds(50)))
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var payload1 = _testEventSender.RequirePayload();

                Assert.Equal(EventDataKind.DiagnosticEvent, payload1.Kind);
                Assert.Equal(1, payload1.EventCount);
                var data1 = LdValue.Parse(payload1.Data);
                Assert.Equal("diagnostic-init", data1.Get("kind").AsString);
                var timestamp1 = data1.Get("creationDate").AsLong;
                Assert.NotEqual(0, timestamp1);

                var payload2 = _testEventSender.RequirePayload();

                Assert.Equal(EventDataKind.DiagnosticEvent, payload2.Kind);
                Assert.Equal(1, payload2.EventCount);
                var data2 = LdValue.Parse(payload2.Data);
                Assert.Equal("diagnostic", data2.Get("kind").AsString);
                var timestamp2 = data2.Get("creationDate").AsLong;
                Assert.InRange(timestamp2, timestamp1, timestamp1 + 1000);

                var payload3 = _testEventSender.RequirePayload();

                Assert.Equal(EventDataKind.DiagnosticEvent, payload3.Kind);
                Assert.Equal(1, payload3.EventCount);
                var data3 = LdValue.Parse(payload3.Data);
                Assert.Equal("diagnostic", data3.Get("kind").AsString);
                var timestamp3 = data2.Get("creationDate").AsLong;
                Assert.InRange(timestamp3, timestamp2, timestamp1 + 1000);
            }
        }

        [Fact]
        public void DiagnosticEventsAreNotSentWhenConfiguredOffline()
        {
            var config = BasicConfig()
                .Offline(true)
                .Events(Components.SendEvents()
                    .EventSender(_testEventSender)
                    .DiagnosticRecordingIntervalNoMinimum(TimeSpan.FromMilliseconds(50)))
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                _testEventSender.RequireNoPayloadSent(TimeSpan.FromMilliseconds(100));

                client.SetOffline(false, TimeSpan.FromMilliseconds(100));

                _testEventSender.RequirePayload();
            }
        }

        [Fact]
        public void DiagnosticEventsAreNotSentWhenNetworkIsUnavailable()
        {
            var connectivityStateManager = new MockConnectivityStateManager(false);
            var config = BasicConfig()
                .ConnectivityStateManager(connectivityStateManager)
                .Events(Components.SendEvents()
                    .EventSender(_testEventSender)
                    .DiagnosticRecordingIntervalNoMinimum(TimeSpan.FromMilliseconds(50)))
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                _testEventSender.RequireNoPayloadSent(TimeSpan.FromMilliseconds(100));

                connectivityStateManager.Connect(true);

                _testEventSender.RequirePayload();
            }
        }

        [Fact]
        public void DiagnosticPeriodicEventsAreNotSentWhenInBackground()
        {
            var mockBackgroundModeManager = new MockBackgroundModeManager();
            var config = BasicConfig()
                .BackgroundModeManager(mockBackgroundModeManager)
                .Events(Components.SendEvents()
                    .EventSender(_testEventSender)
                    .DiagnosticRecordingIntervalNoMinimum(TimeSpan.FromMilliseconds(50)))
                .Build();
            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                mockBackgroundModeManager.UpdateBackgroundMode(true);

                // We will probably still get some periodic events before this mode change is picked
                // up asynchronously, but we should stop getting them soon.
                Assertions.AssertEventually(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(10),
                    () => !_testEventSender.Calls.TryTake(out _, TimeSpan.FromMilliseconds(100)));

                mockBackgroundModeManager.UpdateBackgroundMode(false);

                _testEventSender.RequirePayload();
            }
        }

        [Fact]
        public void ConfigDefaults()
        {
            // Note that in all of the test configurations where the streaming or polling data source
            // is enabled, we're setting a fake HTTP message handler so it doesn't try to do any real
            // HTTP requests that would fail and (depending on timing) disrupt the test.
            TestDiagnosticConfig(
                c => c.Http(Components.HttpConfiguration().MessageHandler(StreamWithInitialData().AsMessageHandler())),
                null,
                ExpectedConfigProps.Base()
                );
        }

        [Fact]
        public void CustomConfigForStreaming()
        {
            TestDiagnosticConfig(
                c => c.DataSource(
                    Components.StreamingDataSource()
                        .BackgroundPollInterval(TimeSpan.FromDays(1))
                        .InitialReconnectDelay(TimeSpan.FromSeconds(2))
                    )
                    .Http(Components.HttpConfiguration().MessageHandler(StreamWithInitialData().AsMessageHandler())),
                null,
                ExpectedConfigProps.Base()
                    .Set("backgroundPollingIntervalMillis", TimeSpan.FromDays(1).TotalMilliseconds)
                    .Set("reconnectTimeMillis", 2000)
                );
        }

        [Fact]
        public void CustomConfigForPolling()
        {
            TestDiagnosticConfig(
                c => c.DataSource(Components.PollingDataSource())
                    .Http(Components.HttpConfiguration().MessageHandler(PollingResponse().AsMessageHandler())),
                null,
                ExpectedConfigProps.Base().WithPollingDefaults()
                );

            TestDiagnosticConfig(
                c => c.DataSource(
                    Components.PollingDataSource()
                        .PollInterval(TimeSpan.FromDays(1))
                    )
                    .Http(Components.HttpConfiguration().MessageHandler(PollingResponse().AsMessageHandler())),
                null,
                ExpectedConfigProps.Base().WithPollingDefaults()
                    .Set("pollingIntervalMillis", TimeSpan.FromDays(1).TotalMilliseconds)
                );
        }

        [Fact]
        public void CustomConfigForEvents()
        {
            TestDiagnosticConfig(
                c => c.Http(Components.HttpConfiguration().MessageHandler(StreamWithInitialData().AsMessageHandler())),
                e => e.AllAttributesPrivate(true)
                    .Capacity(333)
                    .DiagnosticRecordingInterval(TimeSpan.FromMinutes(32))
                    .FlushInterval(TimeSpan.FromMilliseconds(555)),
                ExpectedConfigProps.Base()
                    .Set("allAttributesPrivate", true)
                    .Set("diagnosticRecordingIntervalMillis", TimeSpan.FromMinutes(32).TotalMilliseconds)
                    .Set("eventsCapacity", 333)
                    .Set("eventsFlushIntervalMillis", 555)
                );
        }

        [Fact]
        public void CustomConfigForHTTP()
        {
            TestDiagnosticConfig(
                c => c.Http(
                        Components.HttpConfiguration()
                            .ConnectTimeout(TimeSpan.FromMilliseconds(8888))
                            .ResponseStartTimeout(TimeSpan.FromMilliseconds(9999))
                            .MessageHandler(StreamWithInitialData().AsMessageHandler())
                            .UseReport(true)
                    ),
                null,
                ExpectedConfigProps.Base()
                    .Set("connectTimeoutMillis", 8888)
                    .Set("socketTimeoutMillis", 9999)
                    .Set("useReport", true)
                );

            var proxyUri = new Uri("http://fake");
            var proxy = new WebProxy(proxyUri);
            TestDiagnosticConfig(
                c => c.Http(
                        Components.HttpConfiguration()
                            .Proxy(proxy)
                            .MessageHandler(StreamWithInitialData().AsMessageHandler())
                    ),
                null,
                ExpectedConfigProps.Base()
                    .Set("usingProxy", true)
                );

            var credentials = new CredentialCache();
            credentials.Add(proxyUri, "Basic", new NetworkCredential("user", "pass"));
            var proxyWithAuth = new WebProxy(proxyUri);
            proxyWithAuth.Credentials = credentials;
            TestDiagnosticConfig(
                c => c.Http(
                        Components.HttpConfiguration()
                            .Proxy(proxyWithAuth)
                            .MessageHandler(StreamWithInitialData().AsMessageHandler())
                    ),
                null,
                ExpectedConfigProps.Base()
                    .Set("usingProxy", true)
                    .Set("usingProxyAuthenticator", true)
                );
        }

        [Fact]
        public void TestConfigForServiceEndpoints()
        {
            TestDiagnosticConfig(
                c => c.ServiceEndpoints(Components.ServiceEndpoints().RelayProxy("http://custom"))
                    .Http(Components.HttpConfiguration().MessageHandler(StreamWithInitialData().AsMessageHandler())),
                null,
                ExpectedConfigProps.Base()
                    .Set("customBaseURI", true)
                    .Set("customStreamURI", true)
                    .Set("customEventsURI", true)
                );

            TestDiagnosticConfig(
                c => c.ServiceEndpoints(Components.ServiceEndpoints()
                        .Streaming("http://custom-streaming")
                        .Polling("http://custom-polling")
                        .Events("http://custom-events"))
                    .Http(Components.HttpConfiguration().MessageHandler(StreamWithInitialData().AsMessageHandler())),
                null,
                ExpectedConfigProps.Base()
                    .Set("customBaseURI", true)
                    .Set("customStreamURI", true)
                    .Set("customEventsURI", true)
                );

            TestDiagnosticConfig(
                c => c.ServiceEndpoints(Components.ServiceEndpoints().RelayProxy("http://custom"))
                    .DataSource(Components.PollingDataSource())
                    .Http(Components.HttpConfiguration().MessageHandler(StreamWithInitialData().AsMessageHandler())),
                null,
                ExpectedConfigProps.Base()
                    .WithPollingDefaults()
                    .Set("customBaseURI", true)
                    .Set("customEventsURI", true)
                );
        }

        private void TestDiagnosticConfig(
            Func<ConfigurationBuilder, ConfigurationBuilder> modConfig,
            Func<EventProcessorBuilder, EventProcessorBuilder> modEvents,
            LdValue.ObjectBuilder expected
            )
        {
            var eventsBuilder = Components.SendEvents()
                .EventSender(_testEventSender);
            modEvents?.Invoke(eventsBuilder);
            var configBuilder = BasicConfig()
                .DataSource(Components.StreamingDataSource())
                .Events(eventsBuilder)
                .Http(Components.HttpConfiguration().MessageHandler(Error401Response.AsMessageHandler()));
            modConfig?.Invoke(configBuilder);
            using (var client = TestUtil.CreateClient(configBuilder.Build(), BasicUser, testStartWaitTime))
            {
                var data = ExpectDiagnosticEvent();

                AssertJsonEqual(JsonFromValue("diagnostic-init"), data.Property("kind"));

                AssertJsonEqual(JsonOf(expected.Build().ToJsonString()), data.Property("configuration"));
            }
        }

        private JsonTestValue ExpectDiagnosticEvent()
        {
            var payload = _testEventSender.RequirePayload();
            Assert.Equal(EventDataKind.DiagnosticEvent, payload.Kind);
            Assert.Equal(1, payload.EventCount);
            return JsonOf(payload.Data);
        }
    }

    static class ExpectedConfigProps
    {
        public static LdValue.ObjectBuilder Base() =>
            LdValue.BuildObject()
                .Add("allAttributesPrivate", false)
                .Add("backgroundPollingDisabled", false)
                .Add("backgroundPollingIntervalMillis", Configuration.DefaultBackgroundPollInterval.TotalMilliseconds)
                .Add("customBaseURI", false)
                .Add("connectTimeoutMillis", HttpConfigurationBuilder.DefaultConnectTimeout.TotalMilliseconds)
                .Add("customEventsURI", false)
                .Add("customStreamURI", false)
                .Add("diagnosticRecordingIntervalMillis", EventProcessorBuilder.DefaultDiagnosticRecordingInterval.TotalMilliseconds)
                .Add("evaluationReasonsRequested", false)
                .Add("eventsCapacity", EventProcessorBuilder.DefaultCapacity)
                .Add("eventsFlushIntervalMillis", EventProcessorBuilder.DefaultFlushInterval.TotalMilliseconds)
                .Add("reconnectTimeMillis", StreamingDataSourceBuilder.DefaultInitialReconnectDelay.TotalMilliseconds)
                .Add("socketTimeoutMillis", HttpConfigurationBuilder.DefaultResponseStartTimeout.TotalMilliseconds)
                .Add("startWaitMillis", LdClientDiagnosticEventTest.testStartWaitTime.TotalMilliseconds)
                .Add("streamingDisabled", false)
                .Add("useReport", false)
                .Add("usingProxy", false)
                .Add("usingProxyAuthenticator", false);

        public static LdValue.ObjectBuilder WithPollingDefaults(this LdValue.ObjectBuilder builder) =>
            builder.Set("pollingIntervalMillis", PollingDataSourceBuilder.DefaultPollInterval.TotalMilliseconds)
                .Set("streamingDisabled", true)
                .Remove("customStreamURI")
                .Remove("reconnectTimeMillis");
    }
}
