using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Shared type for AI Configs returned by the SDK. Carries common fields and the
/// <see cref="CreateTracker"/> factory. Cannot be constructed or subclassed outside the SDK.
/// </summary>
public abstract class LdAiConfig
{
    /// <summary>
    /// The key of the AI Config that was evaluated.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Information about the model.
    /// </summary>
    public LdAiConfigTypes.ModelConfig Model { get; }

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public LdAiConfigTypes.ProviderConfig Provider { get; }

    /// <summary>
    /// Whether the config is enabled.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    internal string VariationKey { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    internal int Version { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage. Not exposed on <see cref="Model"/> so
    /// it can't be mistaken for the underlying LLM's own version (e.g. "GPT-5.4").
    /// </summary>
    internal string ModelKey { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    internal int ModelVersion { get; }

    /// <summary>
    /// Factory that produces a tracker for the config. The factory is mode-agnostic — it
    /// operates only on the shared fields (<see cref="Key"/>, <see cref="Model"/>,
    /// <see cref="Provider"/>, <see cref="VariationKey"/>, <see cref="Version"/>,
    /// <see cref="ModelKey"/>, <see cref="ModelVersion"/>), so the same tracker class serves
    /// all config modes.
    /// </summary>
    private readonly Func<LdAiConfig, ILdAiConfigTracker> _trackerFactory;

    /// <summary>
    /// Creates a tracker that emits events related to this config. The returned tracker
    /// is always non-null.
    /// </summary>
    /// <returns>a tracker for this config</returns>
    public ILdAiConfigTracker CreateTracker() => _trackerFactory(this);

    /// <summary>
    /// Constructs the config. Only public derived types in this assembly are intended
    /// to extend this class.
    /// </summary>
    private protected LdAiConfig(
        string key,
        bool enabled,
        string variationKey,
        int version,
        string modelKey,
        int modelVersion,
        LdAiConfigTypes.ModelConfig model,
        LdAiConfigTypes.ProviderConfig provider,
        Func<LdAiConfig, ILdAiConfigTracker> trackerFactory)
    {
        Key = key;
        Enabled = enabled;
        VariationKey = variationKey ?? "";
        Version = version;
        ModelKey = modelKey;
        ModelVersion = modelVersion;
        Model = model ?? new LdAiConfigTypes.ModelConfig("", new Dictionary<string, LdValue>(), new Dictionary<string, LdValue>());
        Provider = provider ?? new LdAiConfigTypes.ProviderConfig("");
        _trackerFactory = trackerFactory ?? throw new ArgumentNullException(nameof(trackerFactory));
    }
}
