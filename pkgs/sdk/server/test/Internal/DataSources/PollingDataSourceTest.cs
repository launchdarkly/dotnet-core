using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Linq;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSystem;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers.HttpTest;
using LaunchDarkly.Logging;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.MockResponses;
using static LaunchDarkly.Sdk.Server.TestHttpUtils;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    public class PollingDataSourceTest : BaseTest
    {
        private readonly FeatureFlag Flag = new FeatureFlagBuilder("flagkey").Build();
        private readonly Segment Segment = new SegmentBuilder("segkey").Version(1).Build();
        private readonly TimeSpan BriefInterval = TimeSpan.FromMilliseconds(20);

        private FullDataSet<ItemDescriptor> AllData =>
            new DataSetBuilder().Flags(Flag).Segments(Segment).Build();

        private readonly CapturingDataSourceUpdates _updateSink = new CapturingDataSourceUpdates();

        public PollingDataSourceTest(ITestOutputHelper testOutput) : base(testOutput) { }

        private IDataSource MakeDataSource(Uri baseUri, Action<ConfigurationBuilder> modConfig = null)
        {
            var builder = BasicConfig()
                .DataSource(Components.PollingDataSource())
                .ServiceEndpoints(Components.ServiceEndpoints().Polling(baseUri));
            modConfig?.Invoke(builder);
            var config = builder.Build();
            return config.DataSource.Build(ContextFrom(config).WithDataSourceUpdates(_updateSink));
        }

        [Theory]
        [InlineData("", "/sdk/latest-all")]
        [InlineData("/basepath", "/basepath/sdk/latest-all")]
        [InlineData("/basepath/", "/basepath/sdk/latest-all")]
        public void PollingRequestHasCorrectUri(string baseUriExtraPath, string expectedPath)
        {
            using (var server = HttpServer.Start(PollingResponse(AllData)))
            {
                var baseUri = new Uri(server.Uri.ToString().TrimEnd('/') + baseUriExtraPath);
                using (var dataSource = MakeDataSource(baseUri))
                {
                    var task = dataSource.Start();

                    var request = server.Recorder.RequireRequest();
                    Assert.Equal(expectedPath, request.Path);
                    Assert.Equal("GET", request.Method);
                }
            }
        }

        [Fact]
        public void SuccessfulRequestCausesDataToBeStoredAndDataSourceInitialized()
        {
            using (var server = HttpServer.Start(PollingResponse(AllData)))
            {
                using (var dataSource = MakeDataSource(server.Uri))
                {
                    var initTask = dataSource.Start();

                    var receivedData = _updateSink.Inits.ExpectValue();
                    AssertHelpers.DataSetsEqual(AllData, receivedData);
                    
                    initTask.Wait(TimeSpan.FromSeconds(1));
                    Assert.True(dataSource.Initialized);

                    Assert.True(initTask.IsCompleted);
                    Assert.False(initTask.IsFaulted);
                }
            }
        }

        [Fact]
        public void SuccessfulRequestCausesDataToBeStoredAndDataSourceInitializedMetadata()
        {
            CapturingDataSourceUpdatesWithHeaders updateSink = new CapturingDataSourceUpdatesWithHeaders();
            using (var server = HttpServer.Start(PollingResponse(AllData)))
            {
                var builder = BasicConfig()
                    .DataSource(Components.PollingDataSource())
                    .ServiceEndpoints(Components.ServiceEndpoints().Polling(server.Uri));
                var config = builder.Build();

                using (var dataSource = config.DataSource.Build(ContextFrom(config).WithDataSourceUpdates(updateSink)))
                {
                    var initTask = dataSource.Start();

                    var receivedData = updateSink.Inits.ExpectValue();
                    AssertHelpers.DataSetsEqual(AllData, receivedData.Item1);
                    // There should be some headers from polling, but we don't want to depend on exact values from
                    // the http server as it isn't implemented in this package.
                    Assert.NotEmpty(receivedData.Item2);

                    // Wait for initialization to complete before checking Initialized flag
                    // to avoid race condition where data is received but flag not yet set
                    bool completed = initTask.Wait(TimeSpan.FromSeconds(1));
                    Assert.True(completed);

                    Assert.True(dataSource.Initialized);
                    Assert.False(initTask.IsFaulted);
                }
            }
        }

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public void UnexpectedHttpErrorEngagesExtendedRegimeAndKeepsPolling(int errorStatus)
        {
            var errorCondition = ServerErrorCondition.FromStatus(errorStatus);

            WithServerErrorCondition(errorCondition, null, (uri, httpConfig, recorder) =>
            {
                // The extended interval is shrunk so the retry is observable; the fields are
                // internal, so no builder method is needed for this.
                var builder = Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval);
                builder._extendedInitialInterval = BriefInterval;

                using (var dataSource = MakeDataSource(uri,
                    c => c.DataSource(builder).Http(httpConfig)))
                {
                    var initTask = dataSource.Start();

                    var status = _updateSink.StatusUpdates.ExpectValue();
                    errorCondition.VerifyDataSourceStatusError(status);

                    recorder.RequireRequest();
                    // Keeps polling rather than giving up, which is the point of the change.
                    recorder.RequireRequest();

                    // Initialization is never resolved by an HTTP error now; the caller's
                    // start-wait timeout decides instead.
                    Assert.False(initTask.Wait(TimeSpan.FromMilliseconds(100)));
                    Assert.False(dataSource.Initialized);

                    errorCondition.VerifyLogMessage(LogCapture);
                    AssertHelpers.LogMessageRegex(LogCapture, true, LogLevel.Info,
                        "engaging extended backoff");
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
                using (var dataSource = MakeDataSource(uri,
                    c => c.DataSource(Components.PollingDataSource().PollInterval(TimeSpan.FromHours(1)))
                        .Http(httpConfig)))
                {
                    dataSource.Start();

                    var status = _updateSink.StatusUpdates.ExpectValue();
                    errorCondition.VerifyDataSourceStatusError(status);
                    Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for recoverable errors");

                    recorder.RequireRequest();
                    recorder.RequireNoRequests(TimeSpan.FromMilliseconds(100));

                    errorCondition.VerifyLogMessage(LogCapture);
                }
            });

            // Verify (with a small polling interval) that it does do another request at the next interval

            WithServerErrorCondition(errorCondition, successResponse, (uri, httpConfig, recorder) =>
            {
                using (var dataSource = MakeDataSource(uri,
                    c => c.DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))
                        .Http(httpConfig)))
                {
                    // Get the count of status updates before starting, so we can check the new one
                    var initialStatusCount = _updateSink.GetAllStatusUpdates().Count;
                    var initTask = dataSource.Start();
                    
                    bool completed = initTask.Wait(TimeSpan.FromSeconds(1));
                    Assert.True(completed);
                    Assert.True(dataSource.Initialized);

                    // Check the status update that occurred during initialization
                    var allUpdates = _updateSink.GetAllStatusUpdates();
                    Assert.True(allUpdates.Count > initialStatusCount, "Expected at least one new status update");
                    var status = allUpdates[initialStatusCount];
                    errorCondition.VerifyDataSourceStatusError(status);
                    Assert.True(status.LastError.Value.Recoverable, "Recoverable should be true for recoverable errors");

                    // We don't check here for a second status update to the Valid state, because that was
                    // done by DataSourceUpdatesImpl when Init was called - our test fixture doesn't do it.

                    recorder.RequireRequest();
                    recorder.RequireRequest();

                    errorCondition.VerifyLogMessage(LogCapture);
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
                using (var dataSource = MakeDataSource(server.Uri,
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
                using (var dataSource = MakeDataSource(server.Uri,
                    c => c.DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))))
                {
                    dataSource.Start();

                    var receivedData = _updateSink.Inits.ExpectValue();
                    AssertHelpers.DataSetsEqual(AllData, receivedData);

                    // We've set it up above so that all requests except the first one return a 304
                    // status, so the data source should *not* push a new data set with Init.
                    _updateSink.Inits.ExpectNoValue(TimeSpan.FromMilliseconds(100));

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
        public void StatusRemainsValidIfServerReturnsNotModifiedStatus()
        {
            var etag = @"""abc123"""; // note that etag strings must be quoted
            var responses = Handlers.SequentialWithLastRepeating(
                Handlers.Header("Etag", etag).Then(PollingResponse(AllData)),
                Handlers.Status(304)
                );

            using (var server = HttpServer.Start(responses))
            {
                using (var dataSource = MakeDataSource(server.Uri,
                    c => c.DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))))
                {
                    dataSource.Start();

                    _updateSink.Inits.ExpectValue();
                    server.Recorder.RequireRequest();
                    server.Recorder.RequireRequest();
                    server.Recorder.RequireRequest();

                    // We've set it up above so that all requests except the first one return a 304
                    // status. That just means the data is unchanged, which is a healthy state, so it
                    // should not be reported as an interruption or logged as a failure.
                    Assert.All(_updateSink.GetAllStatusUpdates(), status =>
                    {
                        Assert.Equal(DataSourceState.Valid, status.State);
                        Assert.Null(status.LastError);
                    });
                    AssertLogMessageRegex(false, Logging.LogLevel.Warn,
                        "Polling for feature flag updates failed");
                }
            }
        }

        [Fact]
        public void ResponseWithNewEtagUpdatesEtag()
        {
            var etag1 = @"""abc123"""; // note that etag strings must be quoted
            var etag2 = @"""def456""";
            var data1 = AllData;
            var data2 = new DataSetBuilder().Flags(Flag, new FeatureFlagBuilder("flag2").Build()).Build();
            var data3 = new DataSetBuilder().Flags(Flag, new FeatureFlagBuilder("flag3").Build()).Build();
            var responses = Handlers.SequentialWithLastRepeating(
                Handlers.Header("Etag", etag1).Then(PollingResponse(data1)),
                Handlers.Status(304),
                Handlers.Header("Etag", etag2).Then(PollingResponse(data2)),
                Handlers.Status(304),
                PollingResponse(data3) // no etag - even though the server will normally send one
                );

            using (var server = HttpServer.Start(responses))
            {
                using (var dataSource = MakeDataSource(server.Uri,
                    c => c.DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))))
                {
                    dataSource.Start();

                    var receivedData1 = _updateSink.Inits.ExpectValue();
                    AssertHelpers.DataSetsEqual(data1, receivedData1);

                    var receivedData2 = _updateSink.Inits.ExpectValue();
                    AssertHelpers.DataSetsEqual(data2, receivedData2);

                    var receivedData3 = _updateSink.Inits.ExpectValue();
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

        #region Reschedule chain and lifecycle

        /// <summary>
        /// A data source updates sink that throws from every status update.
        /// </summary>
        private sealed class ThrowingUpdates : IDataSourceUpdates
        {
            private readonly IDataSourceUpdates _inner;
            public ThrowingUpdates(IDataSourceUpdates inner) { _inner = inner; }

            public IDataStoreStatusProvider DataStoreStatusProvider => _inner.DataStoreStatusProvider;
            public bool Init(FullDataSet<ItemDescriptor> allData) => _inner.Init(allData);
            public bool Upsert(DataKind kind, string key, ItemDescriptor item) =>
                _inner.Upsert(kind, key, item);
            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                // Deliberately lets Off through. Shutdown publishes Off before Dispose(bool) runs,
                // so throwing there would propagate out of Dispose and exercise a separate,
                // already-known shutdown-ordering behavior instead of the poll loop.
                if (newState == DataSourceState.Off)
                {
                    _inner.UpdateStatus(newState, newError);
                    return;
                }
                throw new Exception("deliberate sink failure");
            }
        }

        [Fact]
        public void PollingSurvivesAnExceptionEscapingThePollBody()
        {
            // The reschedule happens in a finally, and TaskExecutor.ScheduleTask logs whatever
            // escapes, so one bad poll must not end polling permanently.
            using (var server = HttpServer.Start(Handlers.Status(503)))
            {
                var builder = BasicConfig()
                    .DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))
                    .ServiceEndpoints(Components.ServiceEndpoints().Polling(server.Uri));
                var config = builder.Build();
                var context = ContextFrom(config)
                    .WithDataSourceUpdates(new ThrowingUpdates(_updateSink));

                using (var dataSource = config.DataSource.Build(context))
                {
                    _ = dataSource.Start();

                    server.Recorder.RequireRequest();
                    server.Recorder.RequireRequest();
                    server.Recorder.RequireRequest();
                }
            }
        }

        [Fact]
        public void DisposeCompletesInitializationForAnyoneWaiting()
        {
            // No failure path resolves initialization any more, so shutdown has to -- otherwise a
            // caller awaiting Start() waits forever.
            using (var server = HttpServer.Start(Handlers.Status(401)))
            {
                var dataSource = MakeDataSource(server.Uri,
                    c => c.DataSource(Components.PollingDataSource()
                        .PollIntervalNoMinimum(BriefInterval)));

                var initTask = dataSource.Start();
                Assert.False(initTask.Wait(TimeSpan.FromMilliseconds(100)));

                dataSource.Dispose();

                Assert.True(initTask.Wait(TimeSpan.FromSeconds(2)));
                Assert.False(initTask.Result);
            }
        }

        [Fact]
        public void DisposeBeforeStartPreventsAnyPolling()
        {
            // The cancellation source spans the object lifetime, so disposing first leaves a later
            // Start() with nothing to do rather than launching a loop that Shutdown cannot reach.
            using (var server = HttpServer.Start(PollingResponse(AllData)))
            {
                var dataSource = MakeDataSource(server.Uri,
                    c => c.DataSource(Components.PollingDataSource()
                        .PollIntervalNoMinimum(BriefInterval)));

                dataSource.Dispose();
                _ = dataSource.Start();

                server.Recorder.RequireNoRequests(TimeSpan.FromMilliseconds(200));
            }
        }

        [Fact]
        public void StartIsIdempotent()
        {
            using (var server = HttpServer.Start(Handlers.Status(503)))
            {
                using (var dataSource = MakeDataSource(server.Uri,
                    c => c.DataSource(Components.PollingDataSource()
                        .PollIntervalNoMinimum(TimeSpan.FromSeconds(30)))))
                {
                    _ = dataSource.Start();
                    _ = dataSource.Start();
                    _ = dataSource.Start();

                    // A single chain, so exactly one request within the long interval.
                    server.Recorder.RequireRequest();
                    server.Recorder.RequireNoRequests(TimeSpan.FromMilliseconds(300));
                }
            }
        }

        #endregion

        #region Poll interval bounds

        [Fact]
        public void PollIntervalIsRaisedToTheDefaultWhenTooSmall()
        {
            var builder = Components.PollingDataSource().PollInterval(TimeSpan.FromMilliseconds(1));

            Assert.Equal(PollingDataSourceBuilder.DefaultPollInterval, builder._pollInterval);
        }

        [Fact]
        public void PollIntervalIsCappedAtTheSchedulableMaximum()
        {
            // Beyond this the wait cannot be scheduled at all, so it is bounded here rather than
            // failing later when the delay is attempted.
            var builder = Components.PollingDataSource().PollInterval(TimeSpan.FromDays(60));

            Assert.Equal(PollingDataSourceBuilder.MaximumPollInterval, builder._pollInterval);
        }

        [Fact]
        public void PollIntervalInRangeIsKept()
        {
            var wanted = TimeSpan.FromMinutes(2);

            Assert.Equal(wanted, Components.PollingDataSource().PollInterval(wanted)._pollInterval);
        }

        #endregion

        #region Extended cadence wired into the data source

        // The strategy is unit-tested in isolation; these check that the data source actually
        // consults it, which a correct-but-unconsulted strategy would pass silently.

        private const int ExtendedMs = 300;

        private IDataSource MakeDataSourceWithBriefExtendedInterval(Uri baseUri)
        {
            var builder = Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval);
            builder._extendedInitialInterval = TimeSpan.FromMilliseconds(ExtendedMs);
            var config = BasicConfig()
                .DataSource(builder)
                .ServiceEndpoints(Components.ServiceEndpoints().Polling(baseUri))
                .Build();
            return config.DataSource.Build(ContextFrom(config).WithDataSourceUpdates(_updateSink));
        }

        [Fact]
        public void UnexpectedFailureUsesTheExtendedCadence()
        {
            using (var server = HttpServer.Start(Handlers.Status(401)))
            {
                using (var dataSource = MakeDataSourceWithBriefExtendedInterval(server.Uri))
                {
                    _ = dataSource.Start();

                    server.Recorder.RequireRequest();
                    var timer = Stopwatch.StartNew();
                    server.Recorder.RequireRequest();
                    timer.Stop();

                    // Jitter puts the wait in (T/2, T], so T/2 is the deterministic floor. The
                    // 20ms configured interval would put this two orders of magnitude lower.
                    Assert.True(timer.ElapsedMilliseconds >= ExtendedMs / 2 - 30,
                        $"second poll came after {timer.ElapsedMilliseconds}ms; the extended " +
                        $"cadence should have delayed it by at least {ExtendedMs / 2}ms");
                }
            }
        }

        [Fact]
        public void OneSuccessfulPollDoesNotLeaveTheExtendedCadence()
        {
            // 401 engages extended, then one success. Two consecutive successes are required to
            // return to normal, so the gap after a single success is still the extended one.
            var handler = Handlers.Sequential(
                Handlers.Status(401),
                PollingResponse(AllData),
                PollingResponse(AllData),
                PollingResponse(AllData));

            using (var server = HttpServer.Start(handler))
            {
                using (var dataSource = MakeDataSourceWithBriefExtendedInterval(server.Uri))
                {
                    _ = dataSource.Start();

                    server.Recorder.RequireRequest();  // the 401
                    server.Recorder.RequireRequest();  // first success
                    var timer = Stopwatch.StartNew();
                    server.Recorder.RequireRequest();  // still extended
                    timer.Stop();

                    Assert.True(timer.ElapsedMilliseconds >= ExtendedMs / 2 - 30,
                        $"poll after a single success came after {timer.ElapsedMilliseconds}ms; " +
                        "one success should not leave the extended cadence");
                }
            }
        }

        [Fact]
        public void TwoConsecutiveSuccessesReturnToTheNormalCadence()
        {
            var handler = Handlers.Sequential(
                Handlers.Status(401),
                PollingResponse(AllData),
                PollingResponse(AllData),
                PollingResponse(AllData));

            using (var server = HttpServer.Start(handler))
            {
                using (var dataSource = MakeDataSourceWithBriefExtendedInterval(server.Uri))
                {
                    _ = dataSource.Start();

                    server.Recorder.RequireRequest();  // the 401
                    server.Recorder.RequireRequest();  // success 1
                    server.Recorder.RequireRequest();  // success 2 -- resets to normal here

                    // Counting requests in a window rather than timing a single gap: a lower bound
                    // on the count is robust where an upper bound on elapsed time would be flaky.
                    var countBefore = server.Recorder.Count;
                    Thread.Sleep(600);
                    var polled = server.Recorder.Count - countBefore;

                    Assert.True(polled >= 3,
                        $"saw {polled} polls in 600ms after two successes; the 20ms normal cadence " +
                        "should produce many, the 300ms extended cadence at most two");
                }
            }
        }

        /// <summary>
        /// Holds a request open until released, so a poll can be guaranteed in flight at a chosen
        /// moment rather than hoped for.
        /// </summary>
        private sealed class BlockingHandler : HttpMessageHandler
        {
            public readonly SemaphoreSlim Started = new SemaphoreSlim(0);
            public readonly SemaphoreSlim Release = new SemaphoreSlim(0);

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Started.Release();
                await Release.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
        }

        [Fact]
        public void StatusUpdatesStopAfterShutdown()
        {
            // A poll in flight when shutdown begins is not interrupted, and typically fails
            // against the HttpClient disposal just closed. Unguarded, that late failure publishes
            // Interrupted over the terminal Off.
            //
            // The request is held open deliberately: an earlier version of this test simply
            // disposed during a fast poll loop and passed with the guard removed, because no poll
            // happened to be in flight and so there was no late write to suppress.
            using (var messageHandler = new BlockingHandler())
            {
                var config = BasicConfig()
                    .DataSource(Components.PollingDataSource().PollIntervalNoMinimum(BriefInterval))
                    .Http(Components.HttpConfiguration().MessageHandler(messageHandler))
                    .Build();
                var dataSource = config.DataSource.Build(
                    ContextFrom(config).WithDataSourceUpdates(_updateSink));

                _ = dataSource.Start();
                Assert.True(messageHandler.Started.Wait(TimeSpan.FromSeconds(5)),
                    "a poll should have started");

                // Shutdown happens with the poll definitively still in flight.
                dataSource.Dispose();

                // Now let it finish and attempt to report.
                messageHandler.Release.Release();

                var deadline = DateTime.UtcNow.AddSeconds(3);
                var reachedOff = false;
                while (!reachedOff && DateTime.UtcNow < deadline)
                {
                    var status = _updateSink.StatusUpdates.ExpectValue(TimeSpan.FromMilliseconds(250));
                    reachedOff = status.State == DataSourceState.Off;
                }
                Assert.True(reachedOff, "shutdown should publish Off");

                _updateSink.StatusUpdates.ExpectNoValue(TimeSpan.FromSeconds(1));
            }
        }

        #endregion
    }
}
