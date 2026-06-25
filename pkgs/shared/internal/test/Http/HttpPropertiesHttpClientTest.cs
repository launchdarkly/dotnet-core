using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.TestHelpers.HttpTest;
using Xunit;

namespace LaunchDarkly.Sdk.Internal.Http
{
    public class HttpPropertiesHttpTest
    {
        // These tests verify that an HTTP client created from HttpProperties has the expected behavior.

        [Fact]
        public async Task ClientUsesCustomHandler()
        {
            HttpResponseMessage testResp = new HttpResponseMessage();
            HttpMessageHandler hmh = new TestHandler(testResp);
            Func<HttpProperties, HttpMessageHandler> hmhf = _ => hmh;

            var hp = HttpProperties.Default.WithHttpMessageHandlerFactory(hmhf);
            Assert.Same(hmhf, hp.HttpMessageHandlerFactory);

            using (var client = hp.NewHttpClient())
            {
                var resp = await client.GetAsync("http://fake");
                Assert.Same(testResp, resp);
            }
        }

        [Fact]
        public async Task ClientUsesConfiguredProxy()
        {
            using (var fakeProxyServer = HttpServer.Start(Handlers.Status(200)))
            {
                var proxy = new WebProxy(fakeProxyServer.Uri);
                var hp = HttpProperties.Default.WithProxy(proxy);

                using (var client = hp.NewHttpClient())
                {
                    var resp = await client.GetAsync("http://fake/");
                    Assert.Equal(200, (int)resp.StatusCode);

                    var request = fakeProxyServer.Recorder.RequireRequest();
                    Assert.Equal(new Uri("http://fake/"), request.Uri);
                }
            }
        }

        [Fact]
        public async Task MessageHandlerUsesConfiguredProxy()
        {
            using (var fakeProxyServer = HttpServer.Start(Handlers.Status(200)))
            {
                var proxy = new WebProxy(fakeProxyServer.Uri);
                var hp = HttpProperties.Default.WithProxy(proxy);
                var handler = hp.NewHttpMessageHandler();

                using (var client = new HttpClient(handler))
                {
                    var resp = await client.GetAsync("http://fake/");
                    Assert.Equal(200, (int)resp.StatusCode);

                    var request = fakeProxyServer.Recorder.RequireRequest();
                    Assert.Equal(new Uri("http://fake/"), request.Uri);
                }
            }
        }

#if NETCOREAPP || NET6_0
        [Fact]
        public async Task ClientUsesConnectTimeout()
        {
            // There doesn't seem to be a way, in .NET's low-level socket API, to set up a TCP listener that
            // doesn't immediately accept incoming connections-- so, we can't directly test what happens if
            // for instance the timeout is 100ms and the connection takes 200ms. We'll do the next best things:
            // 1. verify that it times out if we set a ridiculously low timeout...
            using (var server = HttpServer.Start(Handlers.Status(200)))
            {
                var hp = HttpProperties.Default.WithConnectTimeout(TimeSpan.FromTicks(1));
                using (var client = hp.NewHttpClient())
                {
                    // The exact type of exception thrown by SocketsHttpHandler for a connection timeout is
                    // unfortunately neither consistent nor documented. In practice, it's a TaskCanceledException
                    // in .NET Core 3.1 and .NET 5.0, whereas in .NET Core 2.1 it's an HttpRequestException that
                    // wraps a TaskCanceledException.
                    var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
                        await client.GetAsync(server.Uri));
                    if (ex is HttpRequestException)
                    {
                        Assert.IsType<TaskCanceledException>(ex.InnerException);
                    }
                    else
                    {
                        Assert.IsType<TaskCanceledException>(ex);
                    }
                }
            }

            // ...and 2. verify that if we set a connection timeout that's long enough that the connection
            // is very unlikely to take that long, but still shorter than how long the *response* will take,
            // it does *not* time out.
            using (var server = HttpServer.Start(Handlers.Delay(TimeSpan.FromMilliseconds(400)).Then(Handlers.Status(200))))
            {
                var hp = HttpProperties.Default.WithConnectTimeout(TimeSpan.FromMilliseconds(200));
                using (var client = hp.NewHttpClient())
                {
                    await client.GetAsync(server.Uri);
                }
            }
        }
#endif

        private class TestHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _resp;

            public TestHandler(HttpResponseMessage resp)
            {
                _resp = resp;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_resp);
            }
        }
    }
}
