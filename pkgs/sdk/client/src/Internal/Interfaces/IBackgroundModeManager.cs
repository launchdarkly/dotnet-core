using System;
using LaunchDarkly.Sdk.Client.PlatformSpecific;

namespace LaunchDarkly.Sdk.Client.Internal.Interfaces
{
    internal interface IBackgroundModeManager
    {
        event EventHandler<BackgroundModeChangedEventArgs> BackgroundModeChanged;
    }
}
