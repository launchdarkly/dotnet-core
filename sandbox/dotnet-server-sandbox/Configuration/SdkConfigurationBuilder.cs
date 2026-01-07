using LaunchDarkly.Sdk.Server;

namespace dotnet_server_test_app.Configuration;

/// <summary>
/// Main configuration builder that reads environment variables and builds SDK configuration.
/// </summary>
public static class SdkConfigurationBuilder
{
    /// <summary>
    /// Builds a LaunchDarkly SDK configuration from environment variables.
    /// </summary>
    /// <returns>A configured Configuration object ready for use with LdClient</returns>
    /// <exception cref="ArgumentNullException">Thrown when LAUNCHDARKLY_SDK_KEY is not set</exception>
    /// <exception cref="InvalidOperationException">Thrown when configuration is inconsistent</exception>
    /// <exception cref="ArgumentException">Thrown when an invalid value is provided</exception>
    /// <exception cref="FormatException">Thrown when a value cannot be parsed</exception>
    public static LaunchDarkly.Sdk.Server.Configuration BuildFromEnvironment()
    {
        // 1. Validate SDK key exists (required)
        var sdkKey = Environment.GetEnvironmentVariable(EnvironmentVariables.SdkKey);
        if (string.IsNullOrEmpty(sdkKey))
        {
            throw new ArgumentNullException(
                EnvironmentVariables.SdkKey,
                $"{EnvironmentVariables.SdkKey} environment variable is required");
        }

        // 2. Create configuration builder
        var builder = LaunchDarkly.Sdk.Server.Configuration.Builder(sdkKey);

        // 3. Apply offline mode if specified
        var offline = EnvironmentVariables.ParseBool(
            EnvironmentVariables.Offline,
            EnvironmentVariables.DefaultOffline);
        if (offline)
        {
            builder.Offline(true);
        }

        // 4. Apply DataSystem configuration (if not offline)
        if (!offline)
        {
            var dataSystem = DataSystemConfigurationBuilder.Build();
            if (dataSystem != null)
            {
                builder.DataSystem(dataSystem);
            }
        }

        // 5. Apply start wait time
        var startWaitTimeMs = EnvironmentVariables.ParseInt(
            EnvironmentVariables.StartWaitTimeMs,
            EnvironmentVariables.DefaultStartWaitTimeMs);
        builder.StartWaitTime(TimeSpan.FromMilliseconds(startWaitTimeMs));

        // 6. Build and return
        return builder.Build();
    }
}
