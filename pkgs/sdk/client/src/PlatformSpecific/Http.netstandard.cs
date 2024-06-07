using System;
using System.Net;
using System.Net.Http;
using LaunchDarkly.Sdk.Internal.Http;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class Http
    {
        private static Func<HttpProperties, HttpMessageHandler> PlatformGetHttpMessageHandlerFactory(
            TimeSpan connectTimeout, IWebProxy proxy) =>
            null;
            // Returning null means HttpProperties will use the default .NET implementation,
            // which will take care of configuring the HTTP client with timeouts/proxies.

        private static Exception PlatformTranslateHttpException(Exception e) => e;
    }
}
