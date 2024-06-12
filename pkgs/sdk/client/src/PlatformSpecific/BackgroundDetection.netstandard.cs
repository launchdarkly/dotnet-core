using System;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    // This code is not from MAUI Essentials, though it implements the same abstraction. It is a stub
    // that does nothing, since in .NET Standard there is no notion of an application being in the
    // background or the foreground.

    internal static partial class BackgroundDetection
    {
        private static void PlatformStartListening()
        {
        }

        private static void PlatformStopListening()
        {
        }
    }
}
