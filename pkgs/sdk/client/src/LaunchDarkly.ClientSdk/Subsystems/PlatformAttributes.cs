using System.Globalization;
using LaunchDarkly.Sdk.Client.PlatformSpecific;
using LaunchDarkly.Sdk.EnvReporting;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    internal static class PlatformAttributes
    {
        internal static Layer Layer => new Layer(
            AppInfo.GetAppInfo(),
            DeviceInfo.GetOsInfo(),
            DeviceInfo.GetDeviceInfo(),
            // The InvariantCulture is default if none is set by the application. Microsoft says:
            // "...it is associated with the English language but not with any country/region.."
            // Source: https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.invariantculture
            // In order to avoid returning an empty string (their representation of InvariantCulture) as a context attribute,
            // we will return "en" instead as the closest representation.
            CultureInfo.CurrentCulture.Equals(CultureInfo.InvariantCulture) ? "en" : CultureInfo.CurrentCulture.ToString()
        );
    }
}
