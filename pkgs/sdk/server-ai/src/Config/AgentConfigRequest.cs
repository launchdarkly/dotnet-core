using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents a single agent config request for use with
/// <see cref="LdAiClient.AgentConfigs"/>.
/// </summary>
public class AgentConfigRequest
{
    /// <summary>
    /// The AI Agent Config key.
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// The default config to use if the flag cannot be retrieved or has a mode mismatch.
    /// When null, a disabled config is used as the fallback.
    /// </summary>
    public LdAiAgentConfigDefault DefaultValue { get; set; }

    /// <summary>
    /// Variables used when interpolating Mustache templates in the agent's instructions.
    /// </summary>
    public IReadOnlyDictionary<string, object> Variables { get; set; }
}
