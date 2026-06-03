using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Base type for AI Configs returned by the SDK. Carries common fields and the
/// <see cref="CreateTracker"/> factory. Cannot be constructed or subclassed outside the SDK.
/// </summary>
public abstract class LdAiConfigBase
{
    /// <summary>
    /// The key of the AI Config that was evaluated.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Information about the model.
    /// </summary>
    public ModelConfig Model { get; }

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public ProviderConfig Provider { get; }

    /// <summary>
    /// Whether the config is enabled.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    public string VariationKey { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Factory that produces a tracker for the config. The factory is mode-agnostic — it
    /// operates only on the shared base fields (<see cref="Key"/>, <see cref="Model"/>,
    /// <see cref="Provider"/>, <see cref="VariationKey"/>, <see cref="Version"/>), so the
    /// same tracker class serves all config modes.
    /// </summary>
    private readonly Func<LdAiConfigBase, ILdAiConfigTracker> _trackerFactory;

    /// <summary>
    /// Creates a tracker that emits events related to this config. The returned tracker
    /// is always non-null.
    /// </summary>
    /// <returns>a tracker for this config</returns>
    public ILdAiConfigTracker CreateTracker() => _trackerFactory(this);

    /// <summary>
    /// Constructs the base. Only public derived types in this assembly are intended
    /// to extend this class.
    /// </summary>
    private protected LdAiConfigBase(
        string key,
        bool enabled,
        string variationKey,
        int version,
        ModelConfig model,
        ProviderConfig provider,
        Func<LdAiConfigBase, ILdAiConfigTracker> trackerFactory)
    {
        Key = key;
        Enabled = enabled;
        VariationKey = variationKey ?? "";
        Version = version;
        Model = model ?? new ModelConfig("", new Dictionary<string, LdValue>(), new Dictionary<string, LdValue>());
        Provider = provider ?? new ProviderConfig("");
        _trackerFactory = trackerFactory ?? throw new ArgumentNullException(nameof(trackerFactory));
    }
}
