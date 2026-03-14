namespace dotnet_server_test_app.Configuration;

/// <summary>
/// Centralized environment variable names, defaults, and parsing utilities for SDK configuration.
/// </summary>
public static class EnvironmentVariables
{
    // Environment variable names
    public const string SdkKey = "LAUNCHDARKLY_SDK_KEY";
    public const string Offline = "LAUNCHDARKLY_OFFLINE";
    public const string DataSystemMode = "LAUNCHDARKLY_DATA_SYSTEM_MODE";
    public const string StartWaitTimeMs = "LAUNCHDARKLY_START_WAIT_TIME_MS";
    public const string PersistentStoreType = "LAUNCHDARKLY_PERSISTENT_STORE_TYPE";

    // Redis configuration
    public const string RedisHost = "LAUNCHDARKLY_REDIS_HOST";
    public const string RedisPort = "LAUNCHDARKLY_REDIS_PORT";
    public const string RedisPrefix = "LAUNCHDARKLY_REDIS_PREFIX";
    public const string RedisConnectTimeoutMs = "LAUNCHDARKLY_REDIS_CONNECT_TIMEOUT_MS";
    public const string RedisOperationTimeoutMs = "LAUNCHDARKLY_REDIS_OPERATION_TIMEOUT_MS";

    // DynamoDB configuration
    public const string DynamoDBTableName = "LAUNCHDARKLY_DYNAMODB_TABLE_NAME";
    public const string DynamoDBPrefix = "LAUNCHDARKLY_DYNAMODB_PREFIX";

    // Default values
    public const bool DefaultOffline = false;
    public const string DefaultDataSystemMode = "default";
    public const int DefaultStartWaitTimeMs = 10000;

    public const string DefaultRedisHost = "localhost";
    public const int DefaultRedisPort = 6379;
    public const string DefaultRedisPrefix = "launchdarkly";
    public const int DefaultRedisConnectTimeoutMs = 5000;
    public const int DefaultRedisOperationTimeoutMs = 3000;

    public const string DefaultDynamoDBPrefix = "";

    /// <summary>
    /// Gets a string environment variable value or returns the default if not set.
    /// </summary>
    public static string GetString(string varName, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(varName);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    /// <summary>
    /// Parses an integer environment variable value or returns the default if not set.
    /// Throws FormatException if the value is invalid.
    /// </summary>
    public static int ParseInt(string varName, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(varName);
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, out var result))
        {
            return result;
        }

        throw new FormatException($"Invalid integer value for {varName}: got '{value}', expected a valid integer");
    }

    /// <summary>
    /// Parses a boolean environment variable value or returns the default if not set.
    /// Accepts "true"/"false" (case insensitive).
    /// Throws FormatException if the value is invalid.
    /// </summary>
    public static bool ParseBool(string varName, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(varName);
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        throw new FormatException($"Invalid boolean value for {varName}: got '{value}', expected 'true' or 'false'");
    }

    /// <summary>
    /// Parses an enum environment variable value or returns the default if not set.
    /// Enum values should be provided as strings matching enum names (case insensitive).
    /// Throws ArgumentException if the value is invalid.
    /// </summary>
    public static T ParseEnum<T>(string varName, T defaultValue, string[] validValues) where T : struct
    {
        var value = Environment.GetEnvironmentVariable(varName);
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
        {
            return result;
        }

        var validValuesStr = string.Join(", ", validValues);
        throw new ArgumentException($"Invalid value for {varName}: got '{value}', expected one of: {validValuesStr}");
    }
}
