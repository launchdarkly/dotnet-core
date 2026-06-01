namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Information about the model provider.
/// </summary>
public sealed record LdAiProvider
{
    /// <summary>
    /// The name of the model provider.
    /// </summary>
    public readonly string Name;

    internal LdAiProvider(string name)
    {
        Name = name;
    }
}
