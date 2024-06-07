using System;
using System.Linq;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Json;
using LaunchDarkly.TestHelpers.HttpTest;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Client.MockResponses;
using static LaunchDarkly.Sdk.Client.TestHttpUtils;
using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    public class StreamingDataSourceTest : BaseTest
    {
        private static readonly TimeSpan BriefReconnectDelay = TimeSpan.FromMilliseconds(10);

        private readonly Context simpleUser = Context.New("me");

        private MockDataSourceUpdateSink _updateSink = new MockDataSourceUpdateSink();

        public StreamingDataSourceTest(ITestOutputHelper testOutput) : base(testOutput) { }

        private IDataSource MakeDataSource(Uri baseUri, Context context, Action<ConfigurationBuilder> modConfig = null)
        {
            var builder = BasicConfig()
                .DataSource(Components.StreamingDataSource().InitialReconnectDelay(BriefReconnectDelay))
                .ServiceEndpoints(Components.ServiceEndpoints().Streaming(baseUri).Polling(baseUri));
            modConfig?.Invoke(builder);
            var config = builder.Build();
            return config.DataSource.Build(new LdClientContext(config, context).WithDataSourceUpdateSink(_updateSink));
        }

        private IDataSource MakeDataSourceWithDiagnostics(Uri baseUri, Context context, IDiagnosticStore diagnosticStore)
        {
            var config = BasicConfig()
                .ServiceEndpoints(Components.ServiceEndpoints().Streaming(baseUri).Polling(baseUri))
                .Build();
            var clientContext = new LdClientContext(config, context).WithDiagnostics(null, diagnosticStore)
                .WithDataSourceUpdateSink(_updateSink);
            return Components.StreamingDataSource().InitialReconnectDelay(BriefReconnectDelay).Build(clientContext);
        }

        private void WithDataSourceAndServer(Handler responseHandler, Action<IDataSource, HttpServer, Task> action)
        {
            using (var server = HttpServer.Start(AllowOnlyStreamRequests(responseHandler)))
            {
                using (var dataSource = MakeDataSource(server.Uri, BasicUser))
                {
                    var initTask = dataSource.Start();
                    action(dataSource, server, initTask);
                }
            }
        }

        [Theory]
        [InlineData("", false, "/meval/", "")]
        [InlineData("", true, "/meval/", "?withReasons=true")]
        [InlineData("/basepath", false, "/basepath/meval/", "")]
        [InlineData("/basepath", true, "/basepath/meval/", "?withReasons=true")]
        [InlineData("/basepath/", false, "/basepath/meval/", "")]
        [InlineData("/basepath/", true, "/basepath/meval/", "?withReasons=true")]
        public void RequestHasCorrectUriAndMethodInGetMode(
            string baseUriExtraPath,
            bool withReasons,
            string expectedPathWithoutUser,
            string expectedQuery
            )
        {
            using (var server = HttpServer.Start(StreamWithEmptyData))
            {
                var baseUri = new Uri(server.Uri.ToString().TrimEnd('/') + baseUriExtraPath);
                using (var dataSource = MakeDataSource(baseUri, simpleUser,
                    c => c.EvaluationReasons(withReasons)))
                {
                    dataSource.Start();
                    var req = server.Recorder.RequireRequest();
                    AssertHelpers.ContextsEqual(simpleUser, TestUtil.Base64ContextFromUrlPath(req.Path, expectedPathWithoutUser));
                    Assert.Equal(expectedQuery, req.Query);
                    Assert.Equal("GET", req.Method);
                }
            }
        }

        // REPORT mode is known to fail in Android (ch47341)
#if !ANDROID
        [Theory]
        [InlineData("", false, "/meval", "")]
        [InlineData("", true, "/meval", "?withReasons=true")]
        [InlineData("/basepath", false, "/basepath/meval", "")]
        [InlineData("/basepath", true, "/basepath/meval", "?withReasons=true")]
        [InlineData("/basepath/", false, "/basepath/meval", "")]
        [InlineData("/basepath/", true, "/basepath/meval", "?withReasons=true")]
        public void RequestHasCorrectUriAndMethodAndBodyInReportMode(
            string baseUriExtraPath,
            bool withReasons,
            string expectedPath,
            string expectedQuery
            )
        {
            using (var server = HttpServer.Start(StreamWithEmptyData))
            {
                var baseUri = new Uri(server.Uri.ToString().TrimEnd('/') + baseUriExtraPath);
                using (var dataSource = MakeDataSource(baseUri, simpleUser,
                    c => c.EvaluationReasons(withReasons)
                        .Http(Components.HttpConfiguration().UseReport(true))))
                {
                    dataSource.Start();
                    var req = server.Recorder.RequireRequest();
                    Assert.Equal(expectedPath, req.Path);
                    Assert.Equal(expectedQuery, req.Query);
                    Assert.Equal("REPORT", req.Method);
                    AssertJsonEqual(LdJsonSerialization.SerializeObject(simpleUser), req.Body);
                }
            }
        }
#endif

        [Fact]
        public void PutCausesDataToBeStoredAndDataSourceInitialized()
        {
            var data = new DataSetBuilder()
                .Add("flag1", 1, LdValue.Of(true), 0)
                .Build();

            WithDataSourceAndServer(StreamWithInitialData(data), (dataSource, _, initTask) =>
            {
                var receivedData = _updateSink.ExpectInit(BasicUser);
                AssertHelpers.DataSetsEqual(data, receivedData);

                Assert.True(AsyncUtils.WaitSafely(() => initTask, TimeSpan.FromSeconds(1)));
                Assert.False(initTask.IsFaulted);
                Assert.True(dataSource.Initialized);
            });
        }

        [Fact]
        public void DataSourceIsNotInitializedByDefault()
        {
            WithDataSourceAndServer(StreamThatStaysOpenWithNoEvents, (dataSource, _, initTask) =>
            {
                Assert.False(dataSource.Initialized);
                Assert.False(initTask.IsCompleted);
            });
        }

        [Fact]
        public void PatchUpdatesFlag()
        {
            var flag = new FeatureFlagBuilder().Version(1).Build();
            var patchEvent = PatchEvent(flag.ToJsonString("flag1"));

            WithDataSourceAndServer(StreamWithEmptyInitialDataAndThen(patchEvent), (dataSource, s, t) =>
            {
                _updateSink.ExpectInit(BasicUser);

                var receivedItem = _updateSink.ExpectUpsert(BasicUser, "flag1");
                AssertHelpers.DataItemsEqual(flag.ToItemDescriptor(), receivedItem);
            });
        }

        [Fact]
        public void DeleteDeletesFlag()
        {
            var deleteEvent = DeleteEvent("flag1", 2);

            WithDataSourceAndServer(StreamWithEmptyInitialDataAndThen(deleteEvent), (dataSource, s, t) =>
            {
                _updateSink.ExpectInit(BasicUser);

                var receivedItem = _updateSink.ExpectUpsert(BasicUser, "flag1");
                Assert.Null(receivedItem.Item);
                Assert.Equal(2, receivedItem.Version);
            });
        }

        [Fact]
        public void PingCausesPoll()
        {
            var data = new DataSetBuilder()
                .Add("flag1", 1, LdValue.Of(true), 0)
                .Build();
            var streamWithPing = Handlers.SSE.Start()
                .Then(PingEvent)
                .Then(Handlers.SSE.LeaveOpen());

            using (var pollingServer = HttpServer.Start(PollingResponse(data)))
            {
                using (var streamingServer = HttpServer.Start(streamWithPing))
                {
                    using (var dataSource = MakeDataSource(streamingServer.Uri, BasicUser,
                        c => c.ServiceEndpoints(Components.ServiceEndpoints()
                            .Streaming(streamingServer.Uri).Polling(pollingServer.Uri))))
                    {
                        var initTask = dataSource.Start();

                        pollingServer.Recorder.RequireRequest();

                        var receivedData = _updateSink.ExpectInit(BasicUser);
                        AssertHelpers.DataSetsEqual(data, receivedData);

                        Assert.True(AsyncUtils.WaitSafely(() => initTask, TimeSpan.FromSeconds(1)));
                        Assert.False(initTask.IsFaulted);
                        Assert.True(dataSource.Initialized);
                    }
                }
            }
        }

        [Theory]
        [InlineData(408)]
        [InlineData(429)]
        [InlineData(500)]
        [InlineData(503)]
        [InlineData(ServerErrorCondition.FakeIOException)]
        public void VerifyRecoverableHttpError(int errorStatus)
        {
            var errorCondition = ServerErrorCondition.FromStatus(errorStatus);

            WithServerErrorCondition(errorCondition, StreamWithEmptyData, (uri, httpConfig, recorder) =>
            {
                using (var dataSource = MakeDataSource(uri, BasicUser,
                    c => c.DataSource(Components.StreamingDataSource().InitialReconnectDelay(TimeSpan.Zero))
                        .Http(httpConfig)))
                {
                    var initTask = dataSource.Start();

                    var status = _updateSink.ExpectStatusUpdate();
                    errorCondition.VerifyDataSourceStatusError(status);

                    // We don't check here for a second status update to the Valid state, because that was
                    // done by DataSourceUpdatesImpl when Init was called - our test fixture doesn't do it.

                    _updateSink.ExpectInit(BasicUser);

                    recorder.RequireRequest();
                    recorder.RequireRequest();

                    Assert.True(AsyncUtils.WaitSafely(() => initTask, TimeSpan.FromSeconds(1)));

                    errorCondition.VerifyLogMessage(logCapture);
                }
            });
        }

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public void VerifyUnrecoverableHttpError(int errorStatus)
        {
            var errorCondition = ServerErrorCondition.FromStatus(errorStatus);

            WithServerErrorCondition(errorCondition, StreamWithEmptyData, (uri, httpConfig, recorder) =>
            {
                using (var dataSource = MakeDataSource(uri, BasicUser,
                    c => c.DataSource(Components.StreamingDataSource().InitialReconnectDelay(TimeSpan.Zero))
                        .Http(httpConfig)))
                {
                    var initTask = dataSource.Start();
                    var status = _updateSink.ExpectStatusUpdate();
                    errorCondition.VerifyDataSourceStatusError(status);

                    _updateSink.ExpectNoMoreActions();

                    recorder.RequireRequest();
                    recorder.RequireNoRequests(TimeSpan.FromMilliseconds(100));

                    Assert.True(AsyncUtils.WaitSafely(() => initTask, TimeSpan.FromSeconds(1)));

                    errorCondition.VerifyLogMessage(logCapture);
                }
            });
        }
        
        [Fact]
        public async void StreamInitDiagnosticRecordedOnOpen()
        {
            var mockDiagnosticStore = new MockDiagnosticStore();

            using (var server = HttpServer.Start(StreamWithEmptyData))
            {
                using (var dataSource = MakeDataSourceWithDiagnostics(server.Uri, BasicUser, mockDiagnosticStore))
                {
                    await dataSource.Start();

                    var streamInit = mockDiagnosticStore.StreamInits.ExpectValue();
                    Assert.False(streamInit.Failed);
                }
            }
        }

        [Fact]
        public async void StreamInitDiagnosticRecordedOnError()
        {
            var mockDiagnosticStore = new MockDiagnosticStore();

            using (var server = HttpServer.Start(Error401Response))
            {
                using (var dataSource = MakeDataSourceWithDiagnostics(server.Uri, BasicUser, mockDiagnosticStore))
                {
                    await dataSource.Start();

                    var streamInit = mockDiagnosticStore.StreamInits.ExpectValue();
                    Assert.True(streamInit.Failed);
                }
            }
        }

        [Fact]
        public void UnknownEventTypeDoesNotCauseError()
        {
            VerifyEventDoesNotCauseStreamRestart("weird", "data");
        }

        private void VerifyEventDoesNotCauseStreamRestart(string eventName, string eventData)
        {
            // We'll end another event after that event, so we can see when we've got past the first one
            var events = Handlers.SSE.Event(eventName, eventData)
                .Then(PatchEvent(new FeatureFlagBuilder().Build().ToJsonString("ignore")));

            DoTestAfterEmptyPut(events, server =>
            {
                _updateSink.ExpectUpsert(BasicUser, "ignore");
                
                server.Recorder.RequireNoRequests(TimeSpan.FromMilliseconds(100));

                Assert.Empty(logCapture.GetMessages().Where(m => m.Level == Logging.LogLevel.Error));
            });
        }

        private void DoTestAfterEmptyPut(Handler contentHandler, Action<HttpServer> action)
        {
            var useContentForFirstRequestOnly = Handlers.Sequential(
                StreamWithEmptyInitialDataAndThen(contentHandler),
                StreamThatStaysOpenWithNoEvents
                );
            WithDataSourceAndServer(useContentForFirstRequestOnly, (dataSource, server, initTask) =>
            {
                _updateSink.ExpectInit(BasicUser);
                server.Recorder.RequireRequest();

                action(server);
            });
        }
    }
}
