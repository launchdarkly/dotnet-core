using System;
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
    public string Key { get; init; }

    /// <summary>
    /// Information about the model.
    /// </summary>
    public ModelConfig Model { get; init; }

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public ProviderConfig Provider { get; init; }

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
    /// <param name="trackerFactory">factory that produces a tracker for the eventual instance;
    /// must be non-null</param>
    private protected LdAiConfigBase(Func<LdAiConfigBase, ILdAiConfigTracker> trackerFactory)
    {
        _trackerFactory = trackerFactory ?? throw new ArgumentNullException(nameof(trackerFactory));
    }
}
