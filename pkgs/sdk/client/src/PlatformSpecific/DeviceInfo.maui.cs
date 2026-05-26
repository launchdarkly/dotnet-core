using LaunchDarkly.Sdk.EnvReporting.LayerModels;
using Devices = Microsoft.Maui.Devices;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class DeviceInfo
    {
        internal static OsInfo? GetOsInfo()
        {
            try
            {
                return new OsInfo(
                    PlatformToFamilyString(Devices.DeviceInfo.Current.Platform),
                    Devices.DeviceInfo.Current.Platform.ToString(),
                    Devices.DeviceInfo.Current.VersionString
                );
            }
            catch
            {
                return null;
            }
        }

        internal static LaunchDarkly.Sdk.EnvReporting.LayerModels.DeviceInfo? GetDeviceInfo()
        {
            try
            {
                return new EnvReporting.LayerModels.DeviceInfo(
                    Devices.DeviceInfo.Current.Manufacturer,
                    Devices.DeviceInfo.Current.Model
                );
            }
            catch
            {
                return null;
            }
        }

        private static string PlatformToFamilyString(Devices.DevicePlatform platform) {
            if (platform == Devices.DevicePlatform.Android)
            {
                return "Android";
            }
            else if (platform == Devices.DevicePlatform.iOS ||
                platform == Devices.DevicePlatform.watchOS ||
                platform == Devices.DevicePlatform.tvOS ||
                platform == Devices.DevicePlatform.macOS ||
                platform == Devices.DevicePlatform.MacCatalyst)
            {
                return "Apple";
            }
            else if(platform == Devices.DevicePlatform.WinUI)
            {
                return "Windows";
            }
            else if(platform == Devices.DevicePlatform.Tizen)
            {
                return "Linux";
            }
            else
            {
                return "unknown";
            }
        }
    }
}
