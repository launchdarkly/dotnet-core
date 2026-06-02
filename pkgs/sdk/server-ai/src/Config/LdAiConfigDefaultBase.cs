namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Base type for user-supplied default AI Configs. Carries the data fields common to
/// the public default config types.Cannot be constructed or subclassed outside the SDK.
/// </summary>
public abstract class LdAiConfigDefaultBase
{
    /// <summary>
    /// Information about the model.
    /// </summary>
    public ModelConfig Model { get; init; }

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public ProviderConfig Provider { get; init; }

    /// <summary>
    /// Whether the config is enabled. Null indicates the user did not specify a value.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Only public derived types in this assembly are intended to extend this class.
    /// </summary>
    private protected LdAiConfigDefaultBase() { }
}
