using System;
using System.Net;
using System.Net.Http;
using LaunchDarkly.Sdk.Internal.Http;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class Http
    {
        // NSUrlSessionHandler is the preferred native implementation of HttpMessageHandler on iOS.
        // However, it does not support programmatically setting a proxy, so if a proxy was specified
        // we must fall back to the non-native .NET implementation.
        //
        // Note that we set DisableCaching to true because we do not want iOS to handle HTTP caching
        // for us: in the one area where we want to use it (polling), we keep track of the Etag
        // ourselves, and if the server returns a 304 status we want to be able to see that and know
        // that we don't have to update anything. If iOS did the caching for us, a 304 would be
        // transparently changed to a 200 response with the cached data, and we would end up
        // pointlessly re-parsing and reapplying the response.

        private static Func<HttpProperties, HttpMessageHandler> PlatformGetHttpMessageHandlerFactory(
            TimeSpan connectTimeout,
            IWebProxy proxy
            ) =>
            (proxy is null)
            ? (p => (HttpMessageHandler)new NSUrlSessionHandler() { DisableCaching = true })
            : (Func<HttpProperties, HttpMessageHandler>)null;

        // NSUrlSessionHandler doesn't appear to throw platform-specific exceptions that we care about
        private static Exception PlatformTranslateHttpException(Exception e) => e;
    }
}
