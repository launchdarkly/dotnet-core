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
    /// The prompts associated with the config.
    /// </summary>
    public IReadOnlyList<Message> Messages { get; init; }

    internal LdAiCompletionConfig(string key, bool enabled, string variationKey, int version,
        IEnumerable<Message> messages, ModelConfig model, ProviderConfig provider,
        Func<LdAiConfigBase, ILdAiConfigTracker> trackerFactory)
        : base(trackerFactory)
    {
        Key = key;
        Model = model ?? new ModelConfig("", new Dictionary<string, LdValue>(), new Dictionary<string, LdValue>());
        Provider = provider ?? new ProviderConfig("");
        Messages = messages?.ToList() ?? new List<Message>();
        VariationKey = variationKey ?? "";
        Version = version;
        Enabled = enabled;
    }
}
