using System.ComponentModel;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Shared structure for AI Configs returned by the SDK. This base record exists only
/// to factor out fields common to the public result types (such as
/// <see cref="LdAiCompletionConfig"/>); it is not intended to be referenced directly
/// by SDK consumers and is hidden from IDE autocomplete.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract record LdAiConfigBase
{
    /// <summary>
    /// The key of the AI Config that was evaluated.
    /// </summary>
    public string Key { get; init; }

    /// <summary>
    /// Information about the model.
    /// </summary>
    public LdAiModel Model { get; init; }

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public LdAiProvider Provider { get; init; }

    /// <summary>
    /// Whether the config is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    public string VariationKey { get; init; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Creates a tracker that emits events related to this config. The returned tracker
    /// is always non-null.
    /// </summary>
    /// <returns>a tracker for this config</returns>
    public abstract ILdAiConfigTracker CreateTracker();

    /// <summary>
    /// Only public derived types in this assembly are intended to extend this base record.
    /// </summary>
    private protected LdAiConfigBase() { }
}
