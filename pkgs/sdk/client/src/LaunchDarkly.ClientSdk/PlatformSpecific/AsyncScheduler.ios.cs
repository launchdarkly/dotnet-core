using System;
using Foundation;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class AsyncScheduler
    {
        private static void PlatformScheduleAction(Action a)
        {
            NSRunLoop.Main.BeginInvokeOnMainThread(a.Invoke);
        }
    }
}
