using System;
using System.Threading.Tasks;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class AsyncScheduler
    {
        private static void PlatformScheduleAction(Action a)
        {
            Task.Run(a);
        }
    }
}
