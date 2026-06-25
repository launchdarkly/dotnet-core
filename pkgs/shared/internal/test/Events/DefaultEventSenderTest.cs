using System;
using System.Text;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.TestHelpers.HttpTest;
using Xunit;

using static LaunchDarkly.Sdk.TestUtil;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class DefaultEventSenderTest
    {
        private const string AuthKey = "fake-sdk-key";
        private const string EventsUriPath = "/post-events-here";
        private const string DiagnosticUriPath = "/post-diagnostic-here";
        private const string FakeData = "{\"things\":[]}";
        private static readonly byte[] FakeDataBytes = Encoding.UTF8.GetBytes(FakeData);

        private async Task WithServerAndSender(Handler handler, Func<HttpServer, DefaultEventSender, Task> a)
        {
            using (var server = HttpServer.Start(handler))
            {
                using (var es = MakeSender(server))
                {
                    await a(server, es);
                }
            }
        }

        private DefaultEventSender MakeSender(HttpServer server)
        {
            var config = new EventsConfiguration
            {
                DiagnosticUri = server.Uri.AddPath(DiagnosticUriPath),
                EventsUri = server.Uri.AddPath(EventsUriPath),
                RetryInterval = TimeSpan.FromMilliseconds(10)
            };
            var httpProps = HttpProperties.Default.WithAuthorizationKey(AuthKey);
            return new DefaultEventSender(httpProps, config, NullLogger);
        }

        [Fact]
        public async void AnalyticsEventDataIsSentSuccessfully() =>
            await WithServerAndSender(Handlers.Status(202), async (server, es) =>
            {
                var result = await es.SendEventDataAsync(EventDataKind.AnalyticsEvents, FakeDataBytes, 1);

                Assert.Equal(DeliveryStatus.Succeeded, result.Status);
                Assert.NotNull(result.TimeFromServer);

                var request = server.Recorder.RequireRequest();
                Assert.Equal("POST", request.Method);
                Assert.Equal(EventsUriPath, request.Path);
                Assert.Equal(AuthKey, request.Headers.Get("Authorization"));
                Assert.NotNull(request.Headers.Get("X-LaunchDarkly-Payload-ID"));
                Assert.Equal("4", request.Headers.Get("X-LaunchDarkly-Event-Schema"));
            });

#if !NETFRAMEWORK
        // .NET Framework's implementation of HttpListener, which is used by LaunchDarkly.TestHelpers,
        // doesn't allow setting a custom value for the Date response header. So even though the
        // parsing of this header by DefaultEventSender should still work the same in .NET Framework,
        // we can't test it in this way.
        [Fact]
        public async void EventSenderReadsResponseDateTime() =>
            await WithServerAndSender(Handlers.Status(202).
                Then(Handlers.Header("Date", "Mon, 24 Mar 2014 12:00:00 GMT")), async (server, es) =>
                {
                    var result = await es.SendEventDataAsync(EventDataKind.AnalyticsEvents, FakeDataBytes, 1);

                    Assert.Equal(DeliveryStatus.Succeeded, result.Status);
                    Assert.Equal(new DateTime(2014, 03, 24, 12, 00, 00), result.TimeFromServer);
                });
#endif

        [Fact]
        public async void NewPayloadIdIsGeneratedForEachPayload() =>
            await WithServerAndSender(Handlers.Status(202), async (server, es) =>
            {
                var result1 = await es.SendEventDataAsync(EventDataKind.AnalyticsEvents, FakeDataBytes, 1);
                var result2 = await es.SendEventDataAsync(EventDataKind.AnalyticsEvents, FakeDataBytes, 1);

                Assert.Equal(DeliveryStatus.Succeeded, result1.Status);
                Assert.Equal(DeliveryStatus.Succeeded, result2.Status);

                var req1 = server.Recorder.RequireRequest();
                var req2 = server.Recorder.RequireRequest();
                Assert.NotEqual(
                    req1.Headers.Get("X-LaunchDarkly-Payload-ID"),
                    req2.Headers.Get("X-LaunchDarkly-Payload-ID"));
            });

        [Fact]
        public async void DiagnosticEventDataIsSentSuccessfully() =>
            await WithServerAndSender(Handlers.Status(202), async (server, es) =>
            {
                var result = await es.SendEventDataAsync(EventDataKind.DiagnosticEvent, FakeDataBytes, 1);

                Assert.Equal(DeliveryStatus.Succeeded, result.Status);
                Assert.NotNull(result.TimeFromServer);

                var request = server.Recorder.RequireRequest();
                Assert.Equal("POST", request.Method);
                Assert.Equal(DiagnosticUriPath, request.Path);
                Assert.Equal(AuthKey, request.Headers.Get("Authorization"));
                Assert.Null(request.Headers.Get("X-LaunchDarkly-Payload-ID"));
                Assert.Null(request.Headers.Get("X-LaunchDarkly-Event-Schema"));
            });

        [Theory]
        [InlineData(400)]
        [InlineData(408)]
        [InlineData(429)]
        [InlineData(500)]
        public async void VerifyRecoverableHttpError(int status)
        {
            var handler = Handlers.Sequential(
                Handlers.Status(status), // initial request gets error
                Handlers.Status(202)     // second request gets success
                );
            await WithServerAndSender(handler, async (server, es) =>
            {
                var result = await es.SendEventDataAsync(EventDataKind.AnalyticsEvents, FakeDataBytes, 1);
                Assert.Equal(DeliveryStatus.Succeeded, result.Status);
                Assert.NotNull(result.TimeFromServer);

                var req1 = server.Recorder.RequireRequest();
                var req2 = server.Recorder.RequireRequest();
                Assert.Equal(req1.Body, req2.Body);
                Assert.Equal(req1.Headers.Get("X-LaunchDarkly-Payload-ID"),
                    req2.Headers.Get("X-LaunchDarkly-Payload-ID"));
            });
        }

        [Theory]
        [InlineData(400)]
        [InlineData(408)]
        [InlineData(429)]
        [InlineData(500)]
        public async void VerifyRecoverableHttpErrorIsOnlyRetriedOnce(int status)
        {
            var handler = Handlers.Sequential(
                Handlers.Status(status), // initial request gets error
                Handlers.Status(status), // second request also gets error
                Handlers.Status(202)     // third request would succeed if it got that far
                );

            await WithServerAndSender(handler, async (server, es) =>
            {
                var result = await es.SendEventDataAsync(EventDataKind.AnalyticsEvents, FakeDataBytes, 1);
                Assert.Equal(DeliveryStatus.Failed, result.Status);
                Assert.Null(result.TimeFromServer);

                var req1 = server.Recorder.RequireRequest();
                var req2 = server.Recorder.RequireRequest();
                Assert.Equal(req1.Body, req2.Body);
                Assert.Equal(req1.Headers.Get("X-LaunchDarkly-Payload-ID"),
                    req2.Headers.Get("X-LaunchDarkly-Payload-ID"));

                server.Recorder.RequireNoRequests(TimeSpan.FromMilliseconds(100));
            });
        }

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        public async void VerifyUnrecoverableHttpError(int status) =>
            await WithServerAndSender(Handlers.Status(status), async (server, es) =>
            {
                var result = await es.SendEventDataAsync(EventDataKind.AnalyticsEvents, FakeDataBytes, 1);
                Assert.Equal(DeliveryStatus.FailedAndMustShutDown, result.Status);
                Assert.Null(result.TimeFromServer);

                var request = server.Recorder.RequireRequest();
                server.Recorder.RequireNoRequests(TimeSpan.FromMilliseconds(100));
            });
    }
}
