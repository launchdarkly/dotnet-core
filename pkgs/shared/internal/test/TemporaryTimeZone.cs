using System;
using System.Runtime.InteropServices;

namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// Changes the local time zone of the current process for the lifetime of the instance, which
    /// simulates the effect a daylight saving transition has on the local wall clock.
    /// </summary>
    /// <remarks>
    /// The <c>TZ</c> environment variable is only consulted by .NET on Unix-like platforms, so
    /// tests using this must check <see cref="IsSupported"/> first.
    /// </remarks>
    internal sealed class TemporaryTimeZone : IDisposable
    {
        private readonly string _previousTimeZone;

        internal static bool IsSupported => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        internal TemporaryTimeZone(string timeZoneName)
        {
            _previousTimeZone = Environment.GetEnvironmentVariable("TZ");
            SetLocalTimeZone(timeZoneName);
        }

        internal static void SetLocalTimeZone(string timeZoneName)
        {
            Environment.SetEnvironmentVariable("TZ", timeZoneName);
            TimeZoneInfo.ClearCachedData();
        }

        public void Dispose() => SetLocalTimeZone(_previousTimeZone);
    }
}
