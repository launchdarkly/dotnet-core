using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Graph;

/// <summary>
/// Raw flag value for an agent graph configuration as returned by LaunchDarkly.
/// Returned by <see cref="AgentGraphDefinition.GetConfig"/>.
/// </summary>
public sealed class AgentGraphFlagValue
{
    /// <summary>
    /// The key of the root AIAgentConfig in the graph.
    /// </summary>
    public string Root { get; init; }

    /// <summary>
    /// Object mapping source agent config keys to arrays of outgoing target edges.
    /// Null when no edges are defined.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> Edges { get; init; }

    /// <summary>
    /// LaunchDarkly metadata from the <c>_ldMeta</c> field on the flag value.
    /// Null when a default/fallback value was used (flag not evaluated).
    /// </summary>
    public LdMeta Meta { get; init; }
}

/// <summary>
/// LaunchDarkly metadata from the <c>_ldMeta</c> field on flag values.
/// </summary>
public sealed class LdMeta
{
    /// <summary>
    /// The variation key, if available. Null when a default config was used.
    /// </summary>
    public string VariationKey { get; init; }

    /// <summary>
    /// The version of the flag variation. Defaults to 1.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// Whether the configuration is enabled in the LaunchDarkly dashboard.
    /// Defaults to true. Note: this is distinct from
    /// <see cref="AgentGraphDefinition.Enabled"/>, which reflects the result of ALL
    /// validation checks (metadata enabled + root present + all nodes reachable +
    /// all child configs fetchable).
    /// </summary>
    public bool Enabled { get; init; } = true;
}
