using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;

namespace LaunchDarkly.Sdk.Client.Internal
{
    internal static class StandardEndpoints
    {
        internal static readonly ServiceEndpoints BaseUris = new ServiceEndpoints(
            new Uri("https://clientstream.launchdarkly.com"),
            new Uri("https://clientsdk.launchdarkly.com"),
            new Uri("https://mobile.launchdarkly.com")
            );

        internal static string StreamingGetRequestPath(string contextDataBase64) =>
            "/meval/" + contextDataBase64;
        internal const string StreamingReportRequestPath = "/meval";

        internal static string PollingRequestGetRequestPath(string contextDataBase64) =>
            "msdk/evalx/contexts/" + contextDataBase64;
        internal const string PollingRequestReportRequestPath = "msdk/evalx/context";

        internal const string AnalyticsEventsPostRequestPath = "mobile/events/bulk";

        internal const string DiagnosticEventsPostRequestPath = "mobile/events/diagnostic";

        internal static Uri SelectBaseUri(
            ServiceEndpoints configuredEndpoints,
            Func<ServiceEndpoints, Uri> uriGetter,
            string description,
            Logger errorLogger
            )
        {
            var configuredUri = uriGetter(configuredEndpoints);
            if (configuredUri != null)
            {
                return configuredUri;
            }
            errorLogger.Error(
                "You have set custom ServiceEndpoints without specifying the {0} base URI; connections may not work properly",
                description);
            return uriGetter(BaseUris);
        }

        internal static bool IsCustomUri(
            ServiceEndpoints configuredEndpoints,
            Func<ServiceEndpoints, Uri> uriGetter
            ) =>
            !uriGetter(BaseUris).Equals(uriGetter(configuredEndpoints));
    }
}
