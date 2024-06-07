using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    public class LdClientServiceEndpointsTests : BaseTest
    {
        // These tests verify that the SDK is using the expected base URIs in various configurations.
        // Since we need to be able to intercept requests that would normally go to the production service
        // endpoints, and we don't care about simulating realistic responses, we'll just use a simple
        // HttpMessageHandler stub.

        private static readonly Uri CustomUri = new Uri("http://custom");

        private SimpleRecordingHttpMessageHandler _stubHandler = new SimpleRecordingHttpMessageHandler(401);

        public LdClientServiceEndpointsTests(ITestOutputHelper testOutput) : base(testOutput) { }

        [Fact]
        public void DefaultStreamingDataSourceBaseUri()
        {
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .DataSource(Components.StreamingDataSource())
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .Build(),
                BasicUser))
            {
                var req = _stubHandler.Requests.ExpectValue();
                Assert.Equal(StandardEndpoints.BaseUris.StreamingBaseUri, BaseUriOf(req.RequestUri));
            }
        }

        [Fact]
        public void DefaultPollingDataSourceBaseUri()
        {
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .DataSource(Components.PollingDataSource())
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .Build(),
                BasicUser))
            {
                var req = _stubHandler.Requests.ExpectValue();
                Assert.Equal(StandardEndpoints.BaseUris.PollingBaseUri, BaseUriOf(req.RequestUri));
            }
        }

        [Fact]
        public void DefaultEventsBaseUri()
        {
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .Events(Components.SendEvents().FlushIntervalNoMinimum(TimeSpan.FromMilliseconds(10)))
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .Build(),
                BasicUser))
            {
                var req = _stubHandler.Requests.ExpectValue();
                Assert.Equal(StandardEndpoints.BaseUris.EventsBaseUri, BaseUriOf(req.RequestUri));
            }
        }

        [Fact]
        public void CustomStreamingDataSourceBaseUri()
        {
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .DataSource(Components.StreamingDataSource())
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .ServiceEndpoints(Components.ServiceEndpoints().Streaming(CustomUri).Polling(CustomUri))
                    .Build(),
                BasicUser))
            {
                var req = _stubHandler.Requests.ExpectValue();
                Assert.Equal(CustomUri, BaseUriOf(req.RequestUri));

                Assert.False(logCapture.HasMessageWithRegex(LogLevel.Error,
                    "You have set custom ServiceEndpoints without specifying"));
            }
        }

        [Fact]
        public void CustomPollingDataSourceBaseUri()
        {
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .DataSource(Components.PollingDataSource())
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .ServiceEndpoints(Components.ServiceEndpoints().Polling(CustomUri))
                    .Build(),
                BasicUser))
            {
                var req = _stubHandler.Requests.ExpectValue();
                Assert.Equal(CustomUri, BaseUriOf(req.RequestUri));

                Assert.False(logCapture.HasMessageWithRegex(LogLevel.Error,
                    "You have set custom ServiceEndpoints without specifying"));
            }
        }

        [Fact]
        public void CustomEventsBaseUri()
        {
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .Events(Components.SendEvents().FlushIntervalNoMinimum(TimeSpan.FromMilliseconds(10)))
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .ServiceEndpoints(Components.ServiceEndpoints().Events(CustomUri))
                    .Build(),
                BasicUser))
            {
                var req = _stubHandler.Requests.ExpectValue();
                Assert.Equal(CustomUri, BaseUriOf(req.RequestUri));

                Assert.False(logCapture.HasMessageWithRegex(LogLevel.Error,
                    "You have set custom ServiceEndpoints without specifying"));
            }
        }

        [Fact]
        public void ErrorIsLoggedIfANecessaryUriIsNotSetWhenOtherCustomUrisAreSet()
        {
            var logCapture1 = Logs.Capture();
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .DataSource(Components.StreamingDataSource())
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .Logging(logCapture1)
                    .ServiceEndpoints(Components.ServiceEndpoints().Polling(CustomUri))
                    .Build(),
                BasicUser))
            {
                Assert.True(logCapture1.HasMessageWithRegex(LogLevel.Error,
                    "You have set custom ServiceEndpoints without specifying the Streaming base URI"));
            }

            var logCapture2 = Logs.Capture();
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .DataSource(Components.PollingDataSource())
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .Logging(logCapture2)
                    .ServiceEndpoints(Components.ServiceEndpoints().Events(CustomUri))
                    .Build(),
                BasicUser))
            {
                Assert.True(logCapture2.HasMessageWithRegex(LogLevel.Error,
                    "You have set custom ServiceEndpoints without specifying the Polling base URI"));
            }

            var logCapture3 = Logs.Capture();
            using (var client = TestUtil.CreateClient(
                BasicConfig()
                    .Events(Components.SendEvents())
                    .Http(Components.HttpConfiguration().MessageHandler(_stubHandler))
                    .Logging(logCapture3)
                    .ServiceEndpoints(Components.ServiceEndpoints().Streaming(CustomUri))
                    .Build(),
                BasicUser))
            {
                Assert.True(logCapture3.HasMessageWithRegex(LogLevel.Error,
                    "You have set custom ServiceEndpoints without specifying the Events base URI"));
            }
        }

        private static Uri BaseUriOf(Uri uri) =>
            new Uri(uri.GetComponents(UriComponents.Scheme | UriComponents.HostAndPort | UriComponents.KeepDelimiter, UriFormat.Unescaped));

        private class SimpleRecordingHttpMessageHandler : HttpMessageHandler
        {
            internal readonly EventSink<HttpRequestMessage> Requests = new EventSink<HttpRequestMessage>();
            private int _statusCode;

            public SimpleRecordingHttpMessageHandler(int statusCode)
            {
                _statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Enqueue(request);
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)_statusCode));
            }
        }
    }
}
