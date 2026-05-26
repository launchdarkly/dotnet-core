using System.Globalization;
using Microsoft.Maui.ApplicationModel;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class AppInfo
    {
        internal static ApplicationInfo? GetAppInfo()
        {
            try
            {
                return new ApplicationInfo(
                    Microsoft.Maui.ApplicationModel.AppInfo.Current.PackageName,
                    Microsoft.Maui.ApplicationModel.AppInfo.Current.Name,
                    Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString,
                    Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString);
            }
            catch
            {
                return null;
            }
        }
    }
}
