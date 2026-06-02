namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Information about the model provider.
/// </summary>
public sealed record ProviderConfig
{
    /// <summary>
    /// The name of the model provider.
    /// </summary>
    public readonly string Name;

    internal ProviderConfig(string name)
    {
        Name = name;
    }
}
