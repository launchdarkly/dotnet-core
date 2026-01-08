using LaunchDarkly.Sdk.Server;
#if DEBUG_LOCAL_REFERENCES
using LaunchDarkly.Sdk.Server.Integrations;
#endif
using LaunchDarkly.Sdk.Server.Subsystems;

namespace dotnet_server_test_app.Configuration;

/// <summary>
/// Builds persistent store configurations (Redis or DynamoDB) from environment variables.
/// Note: Redis and DynamoDB support is only available when building with DebugLocalReferences configuration.
/// </summary>
internal static class PersistentStoreConfigurationBuilder
{
    /// <summary>
    /// Builds a persistent store configuration based on LAUNCHDARKLY_PERSISTENT_STORE_TYPE environment variable.
    /// Returns null if no persistent store is configured.
    /// </summary>
    internal static IComponentConfigurer<IDataStore>? Build()
    {
        var storeType = Environment.GetEnvironmentVariable(EnvironmentVariables.PersistentStoreType);

        if (string.IsNullOrEmpty(storeType))
        {
            return null;
        }

        return storeType.ToLower() switch
        {
            "redis" => BuildRedisStore(),
            "dynamodb" => BuildDynamoDBStore(),
            _ => throw new ArgumentException(
                $"Invalid persistent store type: got '{storeType}', expected one of: redis, dynamodb")
        };
    }

#if DEBUG_LOCAL_REFERENCES
    /// <summary>
    /// Builds a Redis persistent store configuration from environment variables.
    /// </summary>
    private static IComponentConfigurer<IDataStore> BuildRedisStore()
    {
        var host = EnvironmentVariables.GetString(EnvironmentVariables.RedisHost, EnvironmentVariables.DefaultRedisHost);
        var port = EnvironmentVariables.ParseInt(EnvironmentVariables.RedisPort, EnvironmentVariables.DefaultRedisPort);
        var prefix = EnvironmentVariables.GetString(EnvironmentVariables.RedisPrefix, EnvironmentVariables.DefaultRedisPrefix);
        var connectTimeoutMs = EnvironmentVariables.ParseInt(
            EnvironmentVariables.RedisConnectTimeoutMs,
            EnvironmentVariables.DefaultRedisConnectTimeoutMs);
        var operationTimeoutMs = EnvironmentVariables.ParseInt(
            EnvironmentVariables.RedisOperationTimeoutMs,
            EnvironmentVariables.DefaultRedisOperationTimeoutMs);

        var redisBuilder = Redis.DataStore()
            .HostAndPort(host, port)
            .Prefix(prefix)
            .ConnectTimeout(TimeSpan.FromMilliseconds(connectTimeoutMs))
            .OperationTimeout(TimeSpan.FromMilliseconds(operationTimeoutMs));

        return Components.PersistentDataStore(redisBuilder);
    }

    /// <summary>
    /// Builds a DynamoDB persistent store configuration from environment variables.
    /// </summary>
    private static IComponentConfigurer<IDataStore> BuildDynamoDBStore()
    {
        var tableName = Environment.GetEnvironmentVariable(EnvironmentVariables.DynamoDBTableName);

        if (string.IsNullOrEmpty(tableName))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariables.DynamoDBTableName} is required when using DynamoDB persistent store");
        }

        var prefix = EnvironmentVariables.GetString(
            EnvironmentVariables.DynamoDBPrefix,
            EnvironmentVariables.DefaultDynamoDBPrefix);

        var builder = DynamoDB.DataStore(tableName);

        if (!string.IsNullOrEmpty(prefix))
        {
            builder.Prefix(prefix);
        }

        return Components.PersistentDataStore(builder);
    }
#else
    /// <summary>
    /// Stub for Redis store when not building with DebugLocalReferences.
    /// </summary>
    private static IComponentConfigurer<IDataStore>? BuildRedisStore()
    {
        Console.WriteLine("WARNING: Redis persistent store is only available when building with DebugLocalReferences configuration.");
        Console.WriteLine("Current build does not include Redis integration. Persistent store will not be used.");
        return null;
    }

    /// <summary>
    /// Stub for DynamoDB store when not building with DebugLocalReferences.
    /// </summary>
    private static IComponentConfigurer<IDataStore>? BuildDynamoDBStore()
    {
        Console.WriteLine("WARNING: DynamoDB persistent store is only available when building with DebugLocalReferences configuration.");
        Console.WriteLine("Current build does not include DynamoDB integration. Persistent store will not be used.");
        return null;
    }
#endif
}
