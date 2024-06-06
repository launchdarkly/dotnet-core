using System;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;
using LaunchDarkly.Sdk.Client.PlatformSpecific;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    internal class DefaultBackgroundModeManager : IBackgroundModeManager
    {
        public event EventHandler<BackgroundModeChangedEventArgs> BackgroundModeChanged
        {
            add
            {
                BackgroundDetection.BackgroundModeChanged += value;
            }
            remove
            {
                BackgroundDetection.BackgroundModeChanged -= value;
            }
        }
    }
}
