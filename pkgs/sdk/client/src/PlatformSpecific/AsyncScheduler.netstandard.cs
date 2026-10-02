using System;
using System.Threading.Tasks;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class AsyncScheduler
    {
        private static volatile Task _lastScheduledTask = Task.CompletedTask;

        private static void PlatformScheduleAction(Action a)
        {
            // Chain the new action to run after the previous one completes
            // This ensures actions run in order while maintaining fire-and-forget behavior
            _lastScheduledTask = _lastScheduledTask.ContinueWith(_ => a());
        }
    }
}
