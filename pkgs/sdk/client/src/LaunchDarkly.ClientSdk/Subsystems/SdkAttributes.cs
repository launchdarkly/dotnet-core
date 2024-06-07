using System.Globalization;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.EnvReporting;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    internal static class SdkAttributes
    {
        internal static Layer Layer => new Layer(new ApplicationInfo(
                SdkPackage.Name,
                SdkPackage.Name,
                SdkPackage.Version,
                SdkPackage.Version),
            null, null, null);
    }
}
