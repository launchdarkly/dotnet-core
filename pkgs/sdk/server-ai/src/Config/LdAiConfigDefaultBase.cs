using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Base type for user-supplied default AI Configs. Carries the data fields common to
/// the public default config types. Cannot be constructed or subclassed outside the SDK.
/// </summary>
public abstract class LdAiConfigDefaultBase
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
    /// Constructs the base. Only public derived types in this assembly are intended
    /// to extend this class.
    /// </summary>
    private protected LdAiConfigDefaultBase(bool? enabled, LdAiConfigTypes.ModelConfig model, LdAiConfigTypes.ProviderConfig provider)
    {
        Enabled = enabled;
        Model = model ?? new LdAiConfigTypes.ModelConfig("", new Dictionary<string, LdValue>(), new Dictionary<string, LdValue>());
        Provider = provider ?? new LdAiConfigTypes.ProviderConfig("");
    }
}
