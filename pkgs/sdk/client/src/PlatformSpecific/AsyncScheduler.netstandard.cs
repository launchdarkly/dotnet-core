using System;
using System.Threading.Tasks;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class AsyncScheduler
    {
        private static void PlatformScheduleAction(Action a)
        {
            try
            {
                // Wait for the task to complete to ensure that actions are executed in the correct order.
                Task.Run(a).Wait();
            }
            catch (Exception)
            {
                // Swallow exceptions to prevent them from propagating to the caller.
            }
        }
    }
}
