using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents an AI Judge Config returned by the SDK, containing model parameters,
/// prompt messages, an evaluation metric key, and a factory that produces a tracker
/// for reporting events related to model usage.
///
/// Instances of this type are produced by the AI client's judge config method;
/// they are not constructed directly by users. To supply a fallback default to the
/// client, use <see cref="LdAiJudgeConfigDefault"/>.
/// </summary>
public sealed class LdAiJudgeConfig : LdAiConfig
{
    /// <summary>
    /// The mode tag emitted in <c>_ldMeta.mode</c> for this config type.
    /// </summary>
    internal const string Mode = "judge";

    /// <summary>
    /// The prompts associated with the judge config.
    /// </summary>
    public IReadOnlyList<LdAiConfigTypes.Message> Messages { get; }

    /// <summary>
    /// The evaluation metric key used to identify this judge's metric.
    /// </summary>
    public string EvaluationMetricKey { get; }

    internal LdAiJudgeConfig(
        string key,
        bool enabled,
        string variationKey,
        int version,
        string modelKey,
        int modelVersion,
        IEnumerable<LdAiConfigTypes.Message> messages,
        string evaluationMetricKey,
        LdAiConfigTypes.ModelConfig model,
        LdAiConfigTypes.ProviderConfig provider,
        Func<LdAiConfig, ILdAiConfigTracker> trackerFactory)
        : base(key, enabled, variationKey, version, modelKey, modelVersion, model, provider, trackerFactory)
    {
        Messages = messages?.ToList() ?? new List<LdAiConfigTypes.Message>();
        EvaluationMetricKey = evaluationMetricKey;
    }
}
