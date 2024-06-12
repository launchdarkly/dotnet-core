using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class Logging
    {
        public static ILogAdapter DefaultAdapter => PlatformDefaultAdapter;
    }
}
