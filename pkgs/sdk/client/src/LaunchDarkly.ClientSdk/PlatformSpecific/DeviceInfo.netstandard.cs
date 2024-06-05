using LaunchDarkly.Sdk.EnvReporting.LayerModels;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class DeviceInfo
    {
        internal static OsInfo? GetOsInfo() => null;

        internal static LaunchDarkly.Sdk.EnvReporting.LayerModels.DeviceInfo? GetDeviceInfo() => null;
    }
}
