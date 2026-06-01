using System.ComponentModel;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Shared structure for user-supplied default AI Configs. This base record exists only
/// to factor out fields common to the public default types (such as
/// <see cref="LdAiCompletionConfigDefault"/>); it is not intended to be referenced
/// directly by SDK consumers and is hidden from IDE autocomplete.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract record LdAiConfigDefaultBase
{
    /// <summary>
    /// Information about the model.
    /// </summary>
    public LdAiModel Model { get; init; }

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public LdAiProvider Provider { get; init; }

    /// <summary>
    /// Whether the config is enabled. Null indicates the user did not specify a value.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Only public derived types in this assembly are intended to extend this base record.
    /// </summary>
    private protected LdAiConfigDefaultBase() { }
}
