using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Client.Integrations;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    /// <summary>
    /// Encapsulates top-level HTTP configuration that applies to all SDK components.
    /// </summary>
    /// <remarks>
    /// Use <see cref="HttpConfigurationBuilder"/> to construct an instance.
    /// </remarks>
    public sealed class HttpConfiguration
    {
        /// <summary>
        /// The network connection timeout.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the time allowed for the underlying HTTP client to connect to the
        /// LaunchDarkly server, for any individual network connection.
        /// </para>
        /// <para>
        /// Not all .NET platforms support setting a connection timeout. It is implemented as
        /// a property of <c>System.Net.Http.SocketsHttpHandler</c> in .NET Core 2.1+ and .NET
        /// 5+, but is unavailable in .NET Framework and .NET Standard. On platforms where it
        /// is not supported, only <see cref="ResponseStartTimeout"/> will be used.
        /// </para>
        /// <para>
        /// Since this is implemented only in <c>SocketsHttpHandler</c>, if you have
        /// specified some other HTTP handler implementation with <see cref="HttpMessageHandler"/>,
        /// the <see cref="ConnectTimeout"/> here will be ignored.
        /// </para>
        /// </remarks>
        /// <seealso cref="ResponseStartTimeout"/>
        public TimeSpan ConnectTimeout { get; }

        /// <summary>
        /// HTTP headers to be added to all HTTP requests made by the SDK.
        /// </summary>
        /// <remarks>
        /// These include <c>Authorization</c>, <c>User-Agent</c>, and any headers that were
        /// specified with <see cref="HttpConfigurationBuilder.CustomHeader(string, string)"/>.
        /// </remarks>
        public IEnumerable<KeyValuePair<string, string>> DefaultHeaders { get; }

        /// <summary>
        /// A custom handler for HTTP requests, or null to use the platform's default handler.
        /// </summary>
        public HttpMessageHandler MessageHandler { get; }

        /// <summary>
        /// The proxy configuration, if any.
        /// </summary>
        /// <remarks>
        /// This is only present if a proxy was specified programmatically with
        /// <see cref="HttpConfigurationBuilder.Proxy(IWebProxy)"/>, not if it was
        /// specified with an environment variable.
        /// </remarks>
        public IWebProxy Proxy { get; }

        /// <summary>
        /// The maximum amount of time to wait for the beginning of an HTTP response.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This limits how long the SDK will wait from the time it begins trying to make a
        /// network connection for an individual HTTP request to the time it starts receiving
        /// any data from the server. It is equivalent to the <c>Timeout</c> property in
        /// <c>HttpClient</c>.
        /// </para>
        /// <para>
        /// It is not the same as the timeout parameter to<see cref= "LdClient.Init(Configuration, Context, TimeSpan)" />,
        /// which limits the time for initializing the SDK regardless of how many individual HTTP
        /// requests are done in that time.
        /// </para>
        /// </remarks>
        /// <seealso cref="ConnectTimeout"/>
        public TimeSpan ResponseStartTimeout { get; }

        /// <summary>
        /// Used internally by SDK code that uses the HttpProperties abstraction from LaunchDarkly.InternalSdk.
        /// </summary>
        internal HttpProperties HttpProperties { get; }

        // NOTE: UseReport is currently internal rather than public because the REPORT verb does not
        // work on every platform (ch47341)
        /// <summary>
        /// Whether to use the HTTP <c>REPORT</c> method for feature flag requests.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default, polling and streaming connections are made with the <c>GET</c> method, with the user data
        /// encoded into the request URI. Using <c>REPORT</c> allows the user data to be sent in the request body
        /// instead, which is somewhat more secure and efficient.
        /// </para>
        /// <para>
        /// However, some mobile platforms and some network gateways do not support <c>REPORT</c>, so it is
        /// disabled by default. You can enable it if you know it is supported in your environment.
        /// </para>
        /// </remarks>
        internal bool UseReport { get; }

        /// <summary>
        /// Constructs an instance, setting all properties.
        /// </summary>
        /// <param name="connectTimeout">value for <see cref="ConnectTimeout"/></param>
        /// <param name="defaultHeaders">value for <see cref="DefaultHeaders"/></param>
        /// <param name="messageHandler">value for <see cref="MessageHandler"/></param>
        /// <param name="proxy">value for <see cref="Proxy"/></param>
        /// <param name="responseStartTimeout">value for <see cref="ResponseStartTimeout"/></param>
        /// <param name="useReport">value for <see cref="UseReport"/></param>
        public HttpConfiguration(
            TimeSpan connectTimeout,
            IEnumerable<KeyValuePair<string, string>> defaultHeaders,
            HttpMessageHandler messageHandler,
            IWebProxy proxy,
            TimeSpan responseStartTimeout,
            bool useReport
            ) :
            this(
                MakeHttpProperties(connectTimeout, defaultHeaders, messageHandler, proxy),
                messageHandler,
                responseStartTimeout,
                useReport
                )
        { }

        internal HttpConfiguration(
            HttpProperties httpProperties,
            HttpMessageHandler messageHandler,
            TimeSpan responseStartTimeout,
            bool useReport
            )
        {
            HttpProperties = httpProperties;
            ConnectTimeout = httpProperties.ConnectTimeout;
            DefaultHeaders = httpProperties.BaseHeaders;
            MessageHandler = messageHandler;
            Proxy = httpProperties.Proxy;
            ResponseStartTimeout = responseStartTimeout;
            UseReport = useReport;
        }

        /// <summary>
        /// Helper method for creating an HTTP client instance using the configured properties.
        /// </summary>
        /// <returns>a client instance</returns>
        public HttpClient NewHttpClient()
        {
            var httpClient = MessageHandler is null ?
                new HttpClient() :
                new HttpClient(MessageHandler, false);
            foreach (var h in DefaultHeaders)
            {
                httpClient.DefaultRequestHeaders.Add(h.Key, h.Value);
            }
            httpClient.Timeout = ResponseStartTimeout;
            return httpClient;
        }

        internal static HttpProperties MakeHttpProperties(
            TimeSpan connectTimeout,
            IEnumerable<KeyValuePair<string, string>> defaultHeaders,
            HttpMessageHandler messageHandler,
            IWebProxy proxy
            )
        {
            var ret = HttpProperties.Default
                .WithConnectTimeout(connectTimeout)
                .WithHttpMessageHandlerFactory(messageHandler is null ?
                    (Func<HttpProperties, HttpMessageHandler>)null :
                    _ => messageHandler)
                .WithProxy(proxy);
            foreach (var kv in defaultHeaders)
            {
                ret = ret.WithHeader(kv.Key, kv.Value);
            }
            return ret;
        }

        internal static HttpConfiguration Default() =>
            new HttpConfiguration(HttpProperties.Default, null, HttpConfigurationBuilder.DefaultResponseStartTimeout, false);
    }
}
