using System;
using UIKit;
using Foundation;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class BackgroundDetection
    {
        private static NSObject _foregroundHandle;
        private static NSObject _backgroundHandle;

        private static void PlatformStartListening()
        {
            _foregroundHandle = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillEnterForegroundNotification, HandleWillEnterForeground);
            _backgroundHandle = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification, HandleWillEnterBackground);
        }

        private static void PlatformStopListening()
        {
            _foregroundHandle = null;
            _backgroundHandle = null;
        }

        private static void HandleWillEnterForeground(NSNotification notification)
        {
            UpdateBackgroundMode(false);
        }

        private static void HandleWillEnterBackground(NSNotification notification)
        {
            UpdateBackgroundMode(true);
        }
    }
}
