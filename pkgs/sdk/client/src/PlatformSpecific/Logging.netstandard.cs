using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class Logging
    {
        internal static ILogAdapter PlatformDefaultAdapter => Logs.ToConsole;
    }
}
