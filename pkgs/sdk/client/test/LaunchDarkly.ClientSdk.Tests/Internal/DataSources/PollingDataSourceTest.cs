using System;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.TestHelpers.HttpTest;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Client.DataModel;
using static LaunchDarkly.Sdk.Client.MockResponses;
using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Client.TestHttpUtils;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    public class PollingDataSourceTest : BaseTest
    {
        private static readonly FeatureFlag Flag = new FeatureFlagBuilder()
            .Version(2).Value(true).Variation(1).Build();
        private static FullDataSet AllData =>
            new DataSetBuilder().Add("flag1", Flag).Build();
        private static readonly TimeSpan BriefInterval = TimeSpan.FromMilliseconds(20);
        private static readonly Context simpleUser = Context.New("me");

        private readonly MockDataSourceUpdateSink _updateSink = new MockDataSourceUpdateSink();

        private IDataSource MakeDataSource(Uri baseUri, Context context, Action<ConfigurationBuilder> modConfig = null)
        {
            var builder = BasicConfig()
                .DataSource(Components.PollingDataSource())
                .ServiceEndpoints(Components.ServiceEndpoints().Polling(baseUri));
            modConfig?.Invoke(builder);
            var config = builder.Build();
            return config.DataSource.Build(new LdClientContext(config, context).WithDataSourceUpdateSink(_updateSink));
        }

        public PollingDataSourceTest(ITestOutputHelper testOutput) : base(testOutput) { }

        [Theory]
        [InlineData("", false, "/msdk/evalx/contexts/", "")]
        [InlineData("", true, "/msdk/evalx/contexts/", "?withReasons=true")]
        [InlineData("/basepath", false, "/basepath/msdk/evalx/contexts/", "")]
        [InlineData("/basepath", true, "/basepath/msdk/evalx/contexts/", "?withReasons=true")]
        [InlineData("/basepath/", false, "/basepath/msdk/evalx/contexts/", "")]
        [InlineData("/basepath/", true, "/basepath/msdk/evalx/contexts/", "?withReasons=true")]
        public void PollingRequestHasCorrectUri(
            string baseUriExtraPath,
            bool withReasons,
            string expectedPathWithoutUser,
            string expectedQuery
            )
        {
            using (var server = HttpServer.Start(PollingResponse(AllData)))
            {
                var baseUri = new Uri(server.Uri.ToString().TrimEnd('/') + baseUriExtraPath);
                using (var dataSource = MakeDataSource(baseUri, simpleUser,
                    c => c.EvaluationReasons(withReasons)))
                {
                    var task = dataSource.Start();

                    var request = server.Recorder.RequireRequest();
                    Assert.Equal("GET", request.Method);
                    AssertHelpers.ContextsEqual(simpleUser, TestUtil.Base64ContextFromUrlPath(request.Path, expectedPathWithoutUser));
                    Assert.Equal(expectedQuery, request.Query);
                }
            }
        }

        [Fact]
        public void SuccessfulRequestCausesDataToBeStoredAndDataSourceInitialized()
        {
            using (var server = HttpServer.Start(PollingResponse(AllData)))
            {
                using (var dataSource = MakeDataSource(server.Uri, BasicUser))
                {
                    var initTask = dataSource.Start();

                    var receivedData = _updateSink.ExpectInit(BasicUser);
                    AssertHelpers.DataSetsEqual(AllData, receivedData);

                    Assert.True(AsyncUtils.WaitSafely(() => initTask, TimeSpan.FromSeconds(1)));
                    Assert.False(initTask.IsFaulted);
                    Assert.True(dataSource.Initialized);
                }
            }
        }

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public void VerifyUnrecoverableHttpError(int errorStatus)
        {
            var errorCondition = ServerErrorCondition.FromStatus(errorStatus);

            WithServerErrorCondition(errorCondition, null, (uri, httpConfig, recorder) =>
            {
                using (var dataSource = MakeDataSource(uri, BasicUser,
                    c => c.DataSource(Components.PollingDataSource().PollInterval(BriefInterval))
                        .Http(httpConfig)))
                {
                    var initTask = dataSource.Start();
                    bool completed = initTask.Wait(TimeSpan.FromSeconds(1));
                    Assert.True(completed);
                    Assert.False(dataSource.Initialized);

                    var status = _updateSink.ExpectStatusUpdate();
                    errorCondition.VerifyDataSourceStatusError(status);

                    recorder.RequireRequest();
                    recorder.RequireNoRequests(TimeSpan.FromMilliseconds(100)); // did not retry

                    errorCondition.VerifyLogMessage(logCapture);
                }
            });
        }

        [Theory]
        [InlineData(408)]
        [InlineData(429)]
        [InlineData(500)]
        [InlineData(ServerErrorCondition.FakeIOException)]
        public void VerifyRecoverableError(int errorStatus)
        {
            var errorCondition = ServerErrorCondition.FromStatus(errorStatus);
            var successResponse = PollingResponse(AllData);

            // Verify that it does not immediately retry the failed request

            WithServerErrorCondition(errorCondition, successResponse, (uri, httpConfig, recorder) =>
            {
                using (var dataSource = MakeDataSource(uri, BasicUser,
                    c => c.DataSource(Components.PollingDataSource().PollInterval(TimeSpan.FromHours(1)))
                        .Http(httpConfig)))
                {
                    dataSource.Start();

                    var status = _updateSink.ExpectStatusUpdate();
                    errorCondition.VerifyDataSourceStatusError(status);

                    recorder.RequireRequest();
                    recorder.RequireNoRequests(TimeSpan.FromMilliseconds(100));

                    errorCondition.VerifyLogMessage(logCapture);
                }
            });

            // Verify (with a small polling interval) that it does do another request at the next interval

            WithServerErrorCondition(errorCondition, successResponse, (uri, httpConfig, recorder) =>
            {
                using (var dataSource = MakeDataSource(uri, BasicUser,
                    c => c.DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))
                        .Http(httpConfig)))
                {
                    var initTask = dataSource.Start();
                    bool completed = initTask.Wait(TimeSpan.FromSeconds(1));
                    Assert.True(completed);
                    Assert.True(dataSource.Initialized);

                    var status = _updateSink.ExpectStatusUpdate();
                    errorCondition.VerifyDataSourceStatusError(status);

                    // We don't check here for a second status update to the Valid state, because that was
                    // done by DataSourceUpdatesImpl when Init was called - our test fixture doesn't do it.

                    recorder.RequireRequest();
                    recorder.RequireRequest();

                    errorCondition.VerifyLogMessage(logCapture);
                }
            });
        }

        [Fact]
        public void EtagIsStoredAndSentWithNextRequest()
        {
            var etag = @"""abc123"""; // note that etag strings must be quoted
            var resp = Handlers.Header("Etag", etag).Then(PollingResponse(AllData));

            using (var server = HttpServer.Start(resp))
            {
                using (var dataSource = MakeDataSource(server.Uri, BasicUser,
                    c => c.DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))))
                {
                    dataSource.Start();

                    var req1 = server.Recorder.RequireRequest();
                    var req2 = server.Recorder.RequireRequest();
                    Assert.Null(req1.Headers.Get("If-None-Match"));
                    Assert.Equal(etag, req2.Headers.Get("If-None-Match"));
                }
            }
        }

        [Fact]
        public void InitIsNotRepeatedIfServerReturnsNotModifiedStatus()
        {
            var etag = @"""abc123"""; // note that etag strings must be quoted
            var responses = Handlers.SequentialWithLastRepeating(
                Handlers.Header("Etag", etag).Then(PollingResponse(AllData)),
                Handlers.Status(304)
                );

            using (var server = HttpServer.Start(responses))
            {
                using (var dataSource = MakeDataSource(server.Uri, BasicUser,
                    c => c.DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))))
                {
                    dataSource.Start();

                    var receivedData = _updateSink.ExpectInit(BasicUser);
                    AssertHelpers.DataSetsEqual(AllData, receivedData);

                    // We've set it up above so that all requests except the first one return a 304
                    // status, so the data source should *not* push a new data set with Init.
                    _updateSink.ExpectNoMoreActions();

                    var req1 = server.Recorder.RequireRequest();
                    var req2 = server.Recorder.RequireRequest();
                    var req3 = server.Recorder.RequireRequest();
                    Assert.Null(req1.Headers.Get("If-None-Match"));
                    Assert.Equal(etag, req2.Headers.Get("If-None-Match"));
                    Assert.Equal(etag, req3.Headers.Get("If-None-Match"));
                }
            }
        }

        [Fact]
        public void ResponseWithNewEtagUpdatesEtag()
        {
            var etag1 = @"""abc123"""; // note that etag strings must be quoted
            var etag2 = @"""def456""";
            var data1 = AllData;
            var data2 = new DataSetBuilder().Add("flag2", new FeatureFlagBuilder().Build()).Build();
            var data3 = new DataSetBuilder().Add("flag3", new FeatureFlagBuilder().Build()).Build();
            var responses = Handlers.SequentialWithLastRepeating(
                Handlers.Header("Etag", etag1).Then(PollingResponse(data1)),
                Handlers.Status(304),
                Handlers.Header("Etag", etag2).Then(PollingResponse(data2)),
                Handlers.Status(304),
                PollingResponse(data3) // no etag - even though the server will normally send one
                );

            using (var server = HttpServer.Start(responses))
            {
                using (var dataSource = MakeDataSource(server.Uri, BasicUser,
                    c => c.DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))))
                {
                    dataSource.Start();

                    var receivedData1 = _updateSink.ExpectInit(BasicUser);
                    AssertHelpers.DataSetsEqual(data1, receivedData1);

                    var receivedData2 = _updateSink.ExpectInit(BasicUser);
                    AssertHelpers.DataSetsEqual(data2, receivedData2);

                    var receivedData3 = _updateSink.ExpectInit(BasicUser);
                    AssertHelpers.DataSetsEqual(data3, receivedData3);

                    var req1 = server.Recorder.RequireRequest();
                    var req2 = server.Recorder.RequireRequest();
                    var req3 = server.Recorder.RequireRequest();
                    var req4 = server.Recorder.RequireRequest();
                    var req5 = server.Recorder.RequireRequest();
                    var req6 = server.Recorder.RequireRequest();
                    Assert.Null(req1.Headers.Get("If-None-Match"));
                    Assert.Equal(etag1, req2.Headers.Get("If-None-Match"));
                    Assert.Equal(etag1, req3.Headers.Get("If-None-Match"));
                    Assert.Equal(etag2, req4.Headers.Get("If-None-Match")); // etag was updated by 3rd response
                    Assert.Equal(etag2, req5.Headers.Get("If-None-Match")); // etag was updated by 3rd response
                    Assert.Null(req6.Headers.Get("If-None-Match")); // etag was cleared by 5th response
                }
            }
        }
    }
}
