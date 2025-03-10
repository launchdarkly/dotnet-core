using LaunchDarkly.Sdk.Internal;

namespace LaunchDarkly.Sdk.Client.Internal
{
    /// <summary>
    /// Defines common information about the SDK itself for usage
    /// in various components.
    /// </summary>
    internal static class SdkPackage
    {
        /// <summary>
        /// The canonical name of this SDK, following the convention of (technology)-(server|client)-sdk.
        /// </summary>
        internal const string Name = "dotnet-client-sdk";

        /// <summary>
        /// The prefix for the User-Agent header, omitting the version string. This may be different than the Name
        /// due to historical reasons.
        /// </summary>
        private const string UserAgentPrefix = "DotnetClientSide";

        /// <summary>
        /// Version of the SDK.
        /// </summary>
        internal static string Version => AssemblyVersions.GetAssemblyVersionStringForType(typeof(LdClient));

        /// <summary>
        /// User-Agent suitable for usage in HTTP requests.
        /// </summary>
        internal static string UserAgent => $"{UserAgentPrefix}/{Version}";

        /// <summary>
        /// The target framework selected at build time.
        /// </summary>
        /// <remarks>
        /// This is the _target framework_ that was selected at build time based
        /// on the application's compatibility requirements; it doesn't tell
        /// anything about the actual OS version.
        /// </remarks>
        internal static string DotNetTargetFramework =>
            // We'll need to update this whenever we add or remove supported target frameworks in the .csproj file.
            // Order of these conditonals matters.  Specific frameworks come before net8.0 intentionally.
#if ANDROID
            "net8.0-android";
#elif IOS
            "net8.0-ios";
#elif MACCATALYST
            "net8.0-maccatalyst";
#elif WINDOWS
            "net8.0-windows";
#elif net8_0
            "net8.0";
#else
            "unknown";
#endif

    }
}
