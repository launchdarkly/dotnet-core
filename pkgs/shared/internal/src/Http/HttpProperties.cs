using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using LaunchDarkly.Sdk.Helpers;

namespace LaunchDarkly.Sdk.Internal.Http
{
    /// <summary>
    /// Internal representation of HTTP options that are supported by both .NET and Xamarin SDKs,
    /// including the logic for constructing the standard set of headers for HTTP requests.
    /// </summary>
    /// <remarks>
    /// This is an immutable struct. The "With" methods for setting properties will return a new
    /// struct based on the current instance.
    /// </remarks>
    public struct HttpProperties
    {
        /// <summary>
        /// An arbitrary default for ConnectTimeout. SDKs should define their own defaults.
        /// </summary>
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// An arbitrary default for ReadTimeout. SDKs should define their own defaults.
        /// </summary>
        public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Headers that should be included in every request.
        /// </summary>
        public ImmutableList<KeyValuePair<string, string>> BaseHeaders { get; }

        /// <summary>
        /// The configured TCP connection timeout.
        /// </summary>
        /// <remarks>
        /// This is used supported in .NET Core and .NET 5+, where <c>SocketsHttpHandler</c> is available.
        /// It is ignored in other platforms, and it is ignored if a custom HTTP handler is specified.
        /// </remarks>
        public TimeSpan ConnectTimeout { get; }

        /// <summary>
        /// A function that transforms platform-specific exceptions if necessary.
        /// </summary>
        /// <remarks>
        /// For mobile platforms where HTTP requests might throw platform-specific exceptions,
        /// you can provide a function to translate them to standard .NET exceptions. By default,
        /// exceptions are not changed.
        /// </remarks>
        public Func<Exception, Exception> HttpExceptionConverter { get; }

        /// <summary>
        /// A function to create an HTTP handler, or null for the standard one.
        /// </summary>
        /// <remarks>
        /// If specified, this factory will be called with the other properties as a parameter. This
        /// may be necessary on platforms like Xamarin where the SDK may want to use a platform-specific
        /// class that needs to be configured at the handler level.
        /// </remarks>
        public Func<HttpProperties, HttpMessageHandler> HttpMessageHandlerFactory { get; }

        /// <summary>
        /// The proxy configuration, if any.
        /// </summary>
        /// <remarks>
        /// This is only present if a proxy was specified programmatically, not if it was
        /// specified with an environment variable.
        /// </remarks>
        public IWebProxy Proxy { get; }

        /// <summary>
        /// The configured TCP socket read timeout.
        /// </summary>
        /// <remarks>
        /// See comments on <see cref="NewHttpClient"/> regarding timeouts.
        /// </remarks>
        public TimeSpan ReadTimeout { get; }

        private HttpProperties(
            ImmutableList<KeyValuePair<string, string>> baseHeaders,
            TimeSpan connectTimeout,
            Func<Exception, Exception> httpExceptionConverter,
            Func<HttpProperties, HttpMessageHandler> httpMessageHandlerFactory,
            IWebProxy proxy,
            TimeSpan readTimeout
        )
        {
            BaseHeaders = baseHeaders;
            ConnectTimeout = connectTimeout;
            HttpExceptionConverter = httpExceptionConverter;
            HttpMessageHandlerFactory = httpMessageHandlerFactory;
            Proxy = proxy;
            ReadTimeout = readTimeout;
        }

        /// <summary>
        /// An instance with default properties.
        /// </summary>
        public static HttpProperties Default =>
            new HttpProperties(
                ImmutableList.Create<KeyValuePair<string, string>>(),
                DefaultConnectTimeout,
                e => e,
                null,
                null,
                DefaultReadTimeout
            );

        public HttpProperties WithConnectTimeout(TimeSpan newConnectTimeout) =>
            new HttpProperties(
                BaseHeaders,
                newConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandlerFactory,
                Proxy,
                ReadTimeout
            );

        public HttpProperties WithHttpMessageHandlerFactory(Func<HttpProperties, HttpMessageHandler> factory) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                HttpExceptionConverter,
                factory,
                Proxy,
                ReadTimeout
            );

        public HttpProperties WithHttpExceptionConverter(Func<Exception, Exception> newHttpExceptionConverter) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                newHttpExceptionConverter,
                HttpMessageHandlerFactory,
                Proxy,
                ReadTimeout
            );

        public HttpProperties WithProxy(IWebProxy newProxy) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandlerFactory,
                newProxy,
                ReadTimeout
            );

        public HttpProperties WithReadTimeout(TimeSpan newReadTimeout) =>
            new HttpProperties(
                BaseHeaders,
                ConnectTimeout,
                HttpExceptionConverter,
                HttpMessageHandlerFactory,
                Proxy,
                newReadTimeout
            );

        public HttpProperties WithAuthorizationKey(string key) =>
            string.IsNullOrEmpty(key) ? this : WithHeader("Authorization", key);

        public HttpProperties WithUserAgent(string userAgent) =>
            string.IsNullOrEmpty(userAgent) ? this : WithHeader("User-Agent", userAgent);

        public HttpProperties WithUserAgent(string userAgentName, string userAgentVersion) =>
            string.IsNullOrEmpty(userAgentName)
                ? this
                : WithHeader("User-Agent", userAgentName + "/" + userAgentVersion);

        public HttpProperties WithWrapper(string wrapperName, string wrapperVersion) =>
            string.IsNullOrEmpty(wrapperName)
                ? this
                : WithHeader("X-LaunchDarkly-Wrapper",
                    string.IsNullOrEmpty(wrapperVersion) ? wrapperName : wrapperName + "/" + wrapperVersion);

        public HttpProperties WithApplicationTags(ApplicationInfo applicationInfo)
        {
            string headerValue = ApplicationTagHeaderValue(applicationInfo);
            if (string.IsNullOrEmpty(headerValue))
            {
                return this;
            }

            return WithHeader("X-LaunchDarkly-Tags", headerValue);
        }

        public HttpProperties WithHeader(string name, string value)
        {
            // Avoiding IEnumberable as it fails to properly optimize for ARM64
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var header in BaseHeaders)
            {
                if (!string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    headers.Add(header);
                }
            }
            headers.Add(new KeyValuePair<string, string>(name, value));

            return new HttpProperties(
                    headers.ToImmutableList(),
                    ConnectTimeout,
                    HttpExceptionConverter,
                    HttpMessageHandlerFactory,
                    Proxy,
                    ReadTimeout
                );
        }

        /// <summary>
        /// Adds BaseHeaders to a request.
        /// </summary>
        /// <param name="req">the HTTP request</param>
        public void AddHeaders(HttpRequestMessage req)
        {
            var rh = req.Headers;
            foreach (var h in BaseHeaders)
            {
                rh.Add(h.Key, h.Value);
            }
        }

        /// <summary>
        /// Creates an <c>HttpClient</c> instance based on this configuration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The client's <c>HttpMessageHandler</c> will be set as follows:
        /// </para>
        /// <list type="bullet">
        /// <item><description> If <c>HttpMessageHandlerFactory</c> was set, it will be called, passing the
        /// other properties as a parameter. </description></item>
        /// <item><description> Otherwise, in .NET Core and .NET 5.0+, the handler will be set to a
        /// <c>SocketsHttpHandler</c>, with the specified <c>ConnectTimeout</c> and <c>Proxy</c>
        /// settings. </description></item>
        /// <item><description> Or, in .NET Framework and .NET Standard, the handler will be left null (to
        /// use the platform default handler) unless <c>Proxy</c> was set, in which case an
        /// <c>HttpClientHandler</c> instance is used. </description></item>
        /// </list>
        /// <para>
        /// The client will <i>not</i> be configured to send <c>BaseHeaders</c> automatically;
        /// headers must still be added to each request. This is because we may want to support
        /// having an application specify its own HTTP client instance.
        /// </para>
        /// <para>
        /// The <c>ReadTimeout</c> property is not part of the <c>HttpClient</c>; it must be
        /// implemented separately by the caller.
        /// </para>
        /// </remarks>
        /// <returns>an HTTP client instance</returns>
        public HttpClient NewHttpClient()
        {
            var handler = (HttpMessageHandlerFactory ?? DefaultHttpMessageHandlerFactory)(this);
            return handler is null ? new HttpClient() : new HttpClient(handler, false);
        }

        /// <summary>
        /// Applies the <c>HttpMessageHandler</c>, <c>Proxy</c>, and <c>ConnectTimeout</c> settings
        /// as appropriate to create a message handler.
        /// </summary>
        /// <returns>the fully configured handler</returns>
        public HttpMessageHandler NewHttpMessageHandler() =>
            (HttpMessageHandlerFactory ?? DefaultHttpMessageHandlerFactory)(this);

        private static HttpMessageHandler DefaultHttpMessageHandlerFactory(HttpProperties props)
        {
            // AutomaticDecompression makes the runtime send "Accept-Encoding: gzip" and transparently
            // decompress gzipped responses, so the SDK accepts compressed streaming/polling/event
            // payloads (matching the transparent-gzip behavior of the Go and Java/OkHttp SDKs).
#if NETCOREAPP
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip,
                ConnectTimeout = props.ConnectTimeout,
                Proxy = props.Proxy
            };
#else
            // Always return a configured handler (not null) so AutomaticDecompression is applied even
            // when no proxy is set; the platform default handler on these frameworks is HttpClientHandler.
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip
            };
            if (props.Proxy != null)
            {
                handler.Proxy = props.Proxy;
            }
            return handler;
#endif
        }

        /// <summary>
        /// Creates the header tag value for the provided <see cref="ApplicationInfo"/>.  Omits properties
        /// that are invalid.
        /// </summary>
        /// <returns>The header tag value. Possibly empty string if no valid properties exist.</returns>
        private static string ApplicationTagHeaderValue(ApplicationInfo applicationInfo)
        {
            var tags = new List<(string, string)>
            {
                // Note these must be in alphabetical order
                ("application-id", applicationInfo.ApplicationId),
                ("application-name", applicationInfo.ApplicationName),
                ("application-version", applicationInfo.ApplicationVersion),
                ("application-version-name", applicationInfo.ApplicationVersionName)
            };
            var parts = new List<string>();
            foreach (var (tagKey, tagVal) in tags)
            {
                if (tagVal == null)
                {
                    continue;
                }

                var error = ValidationUtils.ValidateStringValue(tagVal);
                if (error != null)
                {
                    continue;
                }
                parts.Add($"{tagKey}/{tagVal}");
            }

            return string.Join(" ", parts);
        }
    }
}
