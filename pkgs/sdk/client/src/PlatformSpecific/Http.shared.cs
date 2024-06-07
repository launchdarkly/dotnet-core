using System;
using System.Net;
using System.Net.Http;
using LaunchDarkly.Sdk.Internal.Http;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class Http
    {
        /// <summary>
        /// If our default configuration should use a specific <see cref="HttpMessageHandler"/>
        /// implementation, returns a factory for that implementation.
        /// </summary>
        /// <remarks>
        /// The return value is a factory, rather than having this method itself be the factory,
        /// because of how our shared <c>HttpProperties</c> class is implemented: if you pass a
        /// non-null MessageHandler factory function, then it assumes you will definitely be
        /// returning a fully configured handler, so we would not be able to conditionally fall
        /// back to the default .NET implementation without duplicating the proxy/timeout setup
        /// logic here.
        /// </remarks>
        /// <returns>an HTTP message handler factory or null</returns>
        public static Func<HttpProperties, HttpMessageHandler> GetHttpMessageHandlerFactory(
            TimeSpan connectTimeout,
            IWebProxy proxy
            ) =>
            PlatformGetHttpMessageHandlerFactory(connectTimeout, proxy);

        /// <summary>
        /// Converts any platform-specific exceptions that might be thrown by the platform-specific
        /// HTTP handler to their .NET equivalents.
        /// </summary>
        /// <remarks>
        /// We don't really care about specific network exception classes in our code, but in any case
        /// where we might expose the exception to application code, we want to normalize it to use only
        /// .NET classes.
        /// </remarks>
        /// <param name="e">an exception</param>
        /// <returns>the same exception or a more .NET-appropriate one</returns>
        public static Exception TranslateHttpException(Exception e) =>
            e is HttpRequestException ? e : PlatformTranslateHttpException(e);
    }
}
