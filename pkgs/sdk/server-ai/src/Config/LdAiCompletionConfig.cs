using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
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
public sealed record LdAiCompletionConfig : LdAiConfigBase
{
    /// <summary>
    /// The prompts associated with the config.
    /// </summary>
    public IReadOnlyList<LdAiMessage> Messages { get; init; }

    private readonly Func<LdAiCompletionConfig, ILdAiConfigTracker> _trackerFactory;

    internal LdAiCompletionConfig(string key, bool enabled, IEnumerable<LdAiMessage> messages, Meta meta, Model model, Provider provider,
        Func<LdAiCompletionConfig, ILdAiConfigTracker> trackerFactory)
    {
        Key = key;
        Model = new LdAiModel(model?.Name ?? "", model?.Parameters ?? new Dictionary<string, LdValue>(),
            model?.Custom ?? new Dictionary<string, LdValue>());
        Messages = messages?.ToList() ?? new List<LdAiMessage>();
        VariationKey = meta?.VariationKey ?? "";
        Version = meta?.Version ?? 1;
        Enabled = enabled;
        Provider = new LdAiProvider(provider?.Name ?? "");
        _trackerFactory = trackerFactory ?? throw new ArgumentNullException(nameof(trackerFactory));
    }

    /// <inheritdoc/>
    public override ILdAiConfigTracker CreateTracker() => _trackerFactory(this);
}
