using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Shared type for user-supplied default AI Configs. Carries the data fields common to
/// the mode-specific default config types. Cannot be constructed or subclassed outside the SDK.
/// </summary>
public abstract class LdAiConfigDefault
{
    /// <summary>
    /// Information about the model.
    /// </summary>
    public LdAiConfigTypes.ModelConfig Model { get; }

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public LdAiConfigTypes.ProviderConfig Provider { get; }

    /// <summary>
    /// Whether the config is enabled. Null indicates the user did not specify a value.
    /// </summary>
    public bool? Enabled { get; }

    /// <summary>
    /// Constructs the default config. Only public derived types in this assembly are intended
    /// to extend this class.
    /// </summary>
    private protected LdAiConfigDefault(bool? enabled, LdAiConfigTypes.ModelConfig model, LdAiConfigTypes.ProviderConfig provider)
    {
        Enabled = enabled;
        Model = model ?? new LdAiConfigTypes.ModelConfig("", new Dictionary<string, LdValue>(), new Dictionary<string, LdValue>());
        Provider = provider ?? new LdAiConfigTypes.ProviderConfig("");
    }
}
