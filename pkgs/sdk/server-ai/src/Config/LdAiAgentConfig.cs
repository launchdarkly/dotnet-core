using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents an AI Agent Config returned by the SDK, containing model parameters,
/// agent instructions, available tools, and a factory that produces a tracker for
/// reporting events related to model usage.
///
/// Instances of this type are produced by the AI client's agent config method;
/// they are not constructed directly by users. To supply a fallback default to the
/// client, use <see cref="LdAiAgentConfigDefault"/>.
/// </summary>
public sealed class LdAiAgentConfig : LdAiConfigBase
{
    /// <summary>
    /// The mode tag emitted in <c>_ldMeta.mode</c> for this config type.
    /// </summary>
    internal const string Mode = "agent";

    /// <summary>
    /// The agent's system instructions, which may have been interpolated with Mustache variables.
    /// </summary>
    public string Instructions { get; }

    /// <summary>
    /// The tools available to the agent, keyed by tool name.
    /// </summary>
    public IReadOnlyDictionary<string, ToolConfig> Tools { get; }

    internal LdAiAgentConfig(
        string key,
        bool enabled,
        string variationKey,
        int version,
        string instructions,
        IReadOnlyDictionary<string, ToolConfig> tools,
        ModelConfig model,
        ProviderConfig provider,
        Func<LdAiConfigBase, ILdAiConfigTracker> trackerFactory)
        : base(key, enabled, variationKey, version, model, provider, trackerFactory)
    {
        Instructions = instructions;
        Tools = tools?.ToImmutableDictionary() ?? ImmutableDictionary<string, ToolConfig>.Empty;
    }
}
