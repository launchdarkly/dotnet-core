using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents an AI Completion Config returned by the SDK, containing model parameters,
/// prompt messages, and a factory that produces a tracker for reporting events related
/// to model usage.
///
/// Instances of this type are produced by <see cref="LdAiClient.CompletionConfig"/>;
/// they are not constructed directly by users. To supply a fallback default to the
/// client, use <see cref="LdAiCompletionConfigDefault"/>.
/// </summary>
public sealed class LdAiCompletionConfig : LdAiConfig
{
    /// <summary>
    /// The mode tag emitted in <c>_ldMeta.mode</c> for this config type. Future agent and
    /// judge config types will own their own <c>Mode</c> constants ("agent", "judge").
    /// </summary>
    internal const string Mode = "completion";

    /// <summary>
    /// The prompts associated with the config.
    /// </summary>
    public IReadOnlyList<LdAiConfigTypes.Message> Messages { get; }

    /// <summary>
    /// The tools available to the model, keyed by tool name.
    /// </summary>
    public IReadOnlyDictionary<string, LdAiConfigTypes.Tool> Tools { get; }

    internal LdAiCompletionConfig(string key, bool enabled, string variationKey, int version,
        IEnumerable<LdAiConfigTypes.Message> messages, IReadOnlyDictionary<string, LdAiConfigTypes.Tool> tools,
        LdAiConfigTypes.ModelConfig model, LdAiConfigTypes.ProviderConfig provider,
        Func<LdAiConfig, ILdAiConfigTracker> trackerFactory)
        : base(key, enabled, variationKey, version, model, provider, trackerFactory)
    {
        Messages = messages?.ToList() ?? new List<LdAiConfigTypes.Message>();
        Tools = tools ?? ImmutableDictionary<string, LdAiConfigTypes.Tool>.Empty;
    }
}
