using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Integrations;

namespace dotnet_server_test_app.Configuration;

/// <summary>
/// Builds DataSystem configurations based on environment variables.
/// </summary>
internal static class DataSystemConfigurationBuilder
{
    /// <summary>
    /// Builds a DataSystem configuration based on LAUNCHDARKLY_DATA_SYSTEM_MODE environment variable.
    /// Returns null if using the default mode (which doesn't need explicit configuration).
    /// </summary>
    internal static DataSystemBuilder? Build()
    {
        var mode = EnvironmentVariables.GetString(
            EnvironmentVariables.DataSystemMode,
            EnvironmentVariables.DefaultDataSystemMode);

        return mode.ToLower() switch
        {
            "default" => BuildDefault(),
            "streaming" => BuildStreaming(),
            "polling" => BuildPolling(),
            "daemon" => BuildDaemon(),
            "persistent-store" => BuildPersistentStore(),
            _ => throw new ArgumentException(
                $"Invalid data system mode: got '{mode}', expected one of: default, streaming, polling, daemon, persistent-store")
        };
    }

    /// <summary>
    /// Builds the default DataSystem configuration (CDN + streaming with fallback).
    /// </summary>
    private static DataSystemBuilder BuildDefault()
    {
        return Components.DataSystem().Default();
    }

    /// <summary>
    /// Builds a streaming-only DataSystem configuration.
    /// </summary>
    private static DataSystemBuilder BuildStreaming()
    {
        return Components.DataSystem().Streaming();
    }

    /// <summary>
    /// Builds a polling-only DataSystem configuration.
    /// </summary>
    private static DataSystemBuilder BuildPolling()
    {
        return Components.DataSystem().Polling();
    }

    /// <summary>
    /// Builds a daemon mode DataSystem configuration (read-only from persistent store).
    /// Requires a persistent store to be configured.
    /// </summary>
    private static DataSystemBuilder BuildDaemon()
    {
        var persistentStore = PersistentStoreConfigurationBuilder.Build();

        if (persistentStore == null)
        {
#if DEBUG_LOCAL_REFERENCES
            throw new InvalidOperationException(
                $"{EnvironmentVariables.DataSystemMode} is set to 'daemon' but {EnvironmentVariables.PersistentStoreType} is not configured. " +
                "Daemon mode requires a persistent store.");
#else
            Console.WriteLine("ERROR: Daemon mode requires a persistent store, but persistent store support is not available in this build configuration.");
            Console.WriteLine("Please rebuild with DebugLocalReferences configuration to use Redis or DynamoDB persistent stores.");
            Console.WriteLine("  dotnet build -c DebugLocalReferences");
            throw new InvalidOperationException(
                "Daemon mode requires persistent store support. Rebuild with DebugLocalReferences configuration.");
#endif
        }

        return Components.DataSystem().Daemon(persistentStore);
    }

    /// <summary>
    /// Builds a persistent store DataSystem configuration (default mode + persistent store backup).
    /// Requires a persistent store to be configured.
    /// </summary>
    private static DataSystemBuilder BuildPersistentStore()
    {
        var persistentStore = PersistentStoreConfigurationBuilder.Build();

        if (persistentStore == null)
        {
#if DEBUG_LOCAL_REFERENCES
            throw new InvalidOperationException(
                $"{EnvironmentVariables.DataSystemMode} is set to 'persistent-store' but {EnvironmentVariables.PersistentStoreType} is not configured. " +
                "Persistent-store mode requires a persistent store.");
#else
            Console.WriteLine("ERROR: Persistent-store mode requires a persistent store, but persistent store support is not available in this build configuration.");
            Console.WriteLine("Please rebuild with DebugLocalReferences configuration to use Redis or DynamoDB persistent stores.");
            Console.WriteLine("  dotnet build -c DebugLocalReferences");
            throw new InvalidOperationException(
                "Persistent-store mode requires persistent store support. Rebuild with DebugLocalReferences configuration.");
#endif
        }

        return Components.DataSystem().PersistentStore(persistentStore);
    }
}
