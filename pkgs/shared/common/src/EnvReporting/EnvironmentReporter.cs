using LaunchDarkly.Sdk.EnvReporting.LayerModels;

namespace LaunchDarkly.Sdk.EnvReporting
{
    /// <summary>
    /// An <see cref="IEnvironmentReporter"/> is able to report various attributes
    /// of the environment in which the application is running. If a property is null,
    /// it means the reporter was unable to determine the value.
    /// </summary>
    public interface IEnvironmentReporter
    {
        /// <returns>the <see cref="ApplicationInfo"/> for the application environment</returns>
        ApplicationInfo? ApplicationInfo { get; }
        
        /// <returns>the <see cref="OsInfo"/> for the application environment</returns>
        OsInfo? OsInfo { get; }
        
        /// <returns>the <see cref="DeviceInfo"/> for the application environment</returns>
        DeviceInfo? DeviceInfo { get; }

        /// <returns>the locale for the application environment in the format languagecode2-country/regioncode2</returns>
        string Locale { get; }
    }
}
