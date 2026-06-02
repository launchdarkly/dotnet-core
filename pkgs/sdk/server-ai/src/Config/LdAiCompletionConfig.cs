using System;
using System.Collections.Generic;
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
public sealed class LdAiCompletionConfig : LdAiConfigBase
{
    /// <summary>
    /// The mode tag emitted in <c>_ldMeta.mode</c> for this config type. Future agent and
    /// judge config types will own their own <c>Mode</c> constants ("agent", "judge").
    /// </summary>
    internal const string Mode = "completion";

    /// <summary>
    /// The prompts associated with the config.
    /// </summary>
    public IReadOnlyList<Message> Messages { get; }

    internal LdAiCompletionConfig(string key, bool enabled, string variationKey, int version,
        IEnumerable<Message> messages, ModelConfig model, ProviderConfig provider,
        Func<LdAiConfigBase, ILdAiConfigTracker> trackerFactory)
        : base(key, enabled, variationKey, version, model, provider, trackerFactory)
    {
        Messages = messages?.ToList() ?? new List<Message>();
    }
}
