using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Client.Subsystems;

using static LaunchDarkly.Sdk.Internal.Events.DiagnosticConfigProperties;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    /// <summary>
    /// Contains methods for configuring the SDK's networking behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If you want to set non-default values for any of these properties, create a builder with
    /// <see cref="Components.HttpConfiguration"/>, change its properties with the methods of this class, and
    /// pass it to <see cref="ConfigurationBuilder.Http(HttpConfigurationBuilder)"/>:
    /// </para>
    /// <example>
    /// <code>
    ///     var config = Configuration.Builder(sdkKey)
    ///         .Http(
    ///             Components.HttpConfiguration()
    ///                 .ConnectTimeout(TimeSpan.FromMilliseconds(3000))
    ///         )
    ///         .Build();
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class HttpConfigurationBuilder : IDiagnosticDescription
    {
        /// <summary>
        /// The default value for <see cref="ConnectTimeout(TimeSpan)"/>: 10 seconds.
        /// </summary>
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);
            // deliberately longer than the server-side SDK's default connection timeout

        /// <summary>
        /// The default value for <see cref="ResponseStartTimeout(TimeSpan)"/>: 10 seconds.
        /// </summary>
        public static readonly TimeSpan DefaultResponseStartTimeout = TimeSpan.FromSeconds(10);

        internal TimeSpan _connectTimeout = DefaultConnectTimeout;
        internal List<KeyValuePair<string, string>> _customHeaders = new List<KeyValuePair<string, string>>();
        internal HttpMessageHandler _messageHandler = null;
        internal IWebProxy _proxy = null;
        internal TimeSpan _responseStartTimeout = DefaultResponseStartTimeout;
        internal string _wrapperName = null;
        internal string _wrapperVersion = null;
        internal bool _useReport = false;

        /// <summary>
        /// Sets the network connection timeout.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the time allowed for the underlying HTTP client to connect to the
        /// LaunchDarkly server, for any individual network connection.
        /// </para>
        /// <para>
        /// It is not the same as the timeout parameter to <see cref="LdClient.Init(Configuration, Context, TimeSpan)"/>,
        /// which limits the time for initializing the SDK regardless of how many individual HTTP requests
        /// are done in that time.
        /// </para>
        /// <para>
        /// Not all .NET platforms support setting a connection timeout. It is supported in
        /// .NET Core 2.1+, .NET 5+, and MAUI Android, but not in MAUI iOS. On platforms
        /// where it is not supported, only <see cref="ResponseStartTimeout"/> will be used.
        /// </para>
        /// <para>
        /// Also, since this is implemented (on supported platforms) as part of the standard
        /// <c>HttpMessageHandler</c> implementation for those platforms, if you have specified
        /// some other HTTP handler implementation with <see cref="HttpMessageHandler"/>,
        /// the <see cref="ConnectTimeout"/> here will be ignored.
        /// </para>
        /// </remarks>
        /// <param name="connectTimeout">the timeout</param>
        /// <returns>the builder</returns>
        /// <seealso cref="ResponseStartTimeout"/>
        public HttpConfigurationBuilder ConnectTimeout(TimeSpan connectTimeout)
        {
            _connectTimeout = connectTimeout;
            return this;
        }

        /// <summary>
        /// Specifies a custom HTTP header that should be added to all SDK requests.
        /// </summary>
        /// <remarks>
        /// This may be helpful if you are using a gateway or proxy server that requires a specific header in
        /// requests. You may add any number of headers.
        /// </remarks>
        /// <param name="name">the header name</param>
        /// <param name="value">the header value</param>
        /// <returns>the builder</returns>
        public HttpConfigurationBuilder CustomHeader(string name, string value)
        {
            _customHeaders.Add(new KeyValuePair<string, string>(name, value));
            return this;
        }

        /// <summary>
        /// Specifies a custom HTTP message handler implementation.
        /// </summary>
        /// <remarks>
        /// This is mainly useful for testing, to cause the SDK to use custom logic instead of actual HTTP requests,
        /// but can also be used to customize HTTP behavior on platforms where the default handler is not optimal.
        /// The default is the usual native HTTP handler for the current platform, else <see cref="System.Net.Http.HttpClientHandler"/>.
        /// </remarks>
        /// <param name="messageHandler">the message handler, or null to use the platform's default handler</param>
        /// <returns>the builder</returns>
        public HttpConfigurationBuilder MessageHandler(HttpMessageHandler messageHandler)
        {
            _messageHandler = messageHandler;
            return this;
        }

        /// <summary>
        /// Sets an HTTP proxy for making connections to LaunchDarkly.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is ignored if you have specified a custom message handler with <see cref="MessageHandler(HttpMessageHandler)"/>,
        /// since proxy behavior is implemented by the message handler.
        /// </para>
        /// <para>
        /// Note that this is not the same as the <see href="https://docs.launchdarkly.com/home/relay-proxy">LaunchDarkly
        /// Relay Proxy</see>, which would be set with
        /// <see cref="ServiceEndpointsBuilder.RelayProxy(Uri)"/>.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        ///     // Example of using an HTTP proxy with basic authentication
        ///
        ///     var proxyUri = new Uri("http://my-proxy-host:8080");
        ///     var proxy = new System.Net.WebProxy(proxyUri);
        ///     var credentials = new System.Net.CredentialCache();
        ///     credentials.Add(proxyUri, "Basic",
        ///         new System.Net.NetworkCredential("username", "password"));
        ///     proxy.Credentials = credentials;
        ///
        ///     var config = Configuration.Builder("my-sdk-key")
        ///         .Http(
        ///             Components.HttpConfiguration().Proxy(proxy)
        ///         )
        ///         .Build();
        /// </code>
        /// </example>
        /// <param name="proxy">any implementation of <c>System.Net.IWebProxy</c></param>
        /// <returns>the builder</returns>
        public HttpConfigurationBuilder Proxy(IWebProxy proxy)
        {
            _proxy = proxy;
            return this;
        }

        /// <summary>
        /// Sets the maximum amount of time to wait for the beginning of an HTTP response.
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
        /// which limits the time for initializing the SDK regardless of how many individual HTTP requests
        /// are done in that time.
        /// </para>
        /// </remarks>
        /// <param name="responseStartTimeout">the timeout</param>
        /// <returns>the builder</returns>
        /// <seealso cref="ConnectTimeout"/>
        public HttpConfigurationBuilder ResponseStartTimeout(TimeSpan responseStartTimeout)
        {
            _responseStartTimeout = responseStartTimeout;
            return this;
        }

        /// <summary>
        /// Sets whether to use the HTTP <c>REPORT</c> method for feature flag requests.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default, polling and streaming connections are made with the <c>GET</c> method, with the user data
        /// encoded into the request URI. Using <c>REPORT</c> allows the user data to be sent in the request body
        /// instead, which is somewhat more secure and efficient.
        /// </para>
        /// <para>
        /// However, the <c>REPORT</c> method is not always supported: Android (in the versions tested so far)
        /// does not allow it, and some network gateways do not allow it. Therefore it is disabled in the SDK
        /// by default. You can enable it if you know your code will not be running on Android and not connecting
        /// through a gateway/proxy that disallows <c>REPORT</c>.
        /// </para>
        /// </remarks>
        /// <param name="useReport">true to enable the REPORT method</param>
        /// <returns>the builder</returns>
#if !ANDROID
        public HttpConfigurationBuilder UseReport(bool useReport)
#else
        internal HttpConfigurationBuilder UseReport(bool useReport)
#endif
        {
            _useReport = useReport;
            return this;
        }

        /// <summary>
        /// For use by wrapper libraries to set an identifying name for the wrapper being used.
        /// </summary>
        /// <remarks>
        /// This will be included in a header during requests to the LaunchDarkly servers to allow recording
        /// metrics on the usage of these wrapper libraries.
        /// </remarks>
        /// <param name="wrapperName">an identifying name for the wrapper library</param>
        /// <param name="wrapperVersion">version string for the wrapper library</param>
        /// <returns>the builder</returns>
        public HttpConfigurationBuilder Wrapper(string wrapperName, string wrapperVersion)
        {
            _wrapperName = wrapperName;
            _wrapperVersion = wrapperVersion;
            return this;
        }

        /// <summary>
        /// Called internally by the SDK to create an implementation instance. Applications do not need
        /// to call this method.
        /// </summary>
        /// <param name="authKey">Key for authenticating with LD service</param>
        /// <param name="applicationInfo">Application Info for this application environment</param>
        /// <returns>an <see cref="HttpConfiguration"/></returns>
        public HttpConfiguration CreateHttpConfiguration(string authKey, ApplicationInfo? applicationInfo) =>
            new HttpConfiguration(
                MakeHttpProperties(authKey, applicationInfo),
                _messageHandler,
                _responseStartTimeout,
                _useReport
                );

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.BuildObject()
                .WithHttpProperties(MakeHttpProperties(context.MobileKey, context.EnvironmentReporter.ApplicationInfo))
                .Add("useReport", _useReport)
                .Set("socketTimeoutMillis", _responseStartTimeout.TotalMilliseconds)
                // WithHttpProperties normally sets socketTimeoutMillis to the ReadTimeout value,
                // which is more correct, but we can't really set ReadTimeout in this SDK
                .Build();

        private HttpProperties MakeHttpProperties(string authToken, ApplicationInfo? applicationInfo)
        {
            Func<HttpProperties, HttpMessageHandler> handlerFn;
            if (_messageHandler is null)
            {
                handlerFn = PlatformSpecific.Http.GetHttpMessageHandlerFactory(_connectTimeout, _proxy);
            }
            else
            {
                handlerFn = p => _messageHandler;
            }

            var httpProperties = HttpProperties.Default
                .WithAuthorizationKey(authToken)
                .WithConnectTimeout(_connectTimeout)
                .WithHttpMessageHandlerFactory(handlerFn)
                .WithProxy(_proxy)
                .WithUserAgent(SdkPackage.UserAgent)
                .WithWrapper(_wrapperName, _wrapperVersion);

            if (applicationInfo.HasValue)
            {
                httpProperties = httpProperties.WithApplicationTags(applicationInfo.Value);
            }

            foreach (var kv in _customHeaders)
            {
                httpProperties = httpProperties.WithHeader(kv.Key, kv.Value);
            }
            return httpProperties;
        }
    }
}
