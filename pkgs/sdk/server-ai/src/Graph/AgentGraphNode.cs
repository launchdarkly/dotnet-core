using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;

namespace LaunchDarkly.Sdk.Server.Ai.Graph;

/// <summary>
/// Represents a single node within an agent graph.
/// Each node wraps an <see cref="LdAiAgentConfig"/> and carries the outgoing edges to
/// its children. Use the config's tracker (via <c>Config.CreateTracker()</c>) to record
/// node-level metrics.
/// </summary>
public sealed class AgentGraphNode
{
    /// <summary>
    /// The agent config key for this node.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The agent config for this node.
    /// </summary>
    public LdAiAgentConfig Config { get; }

    /// <summary>
    /// The outgoing edges from this node to its children.
    /// </summary>
    public IReadOnlyList<GraphEdge> Edges { get; }

    /// <summary>
    /// Whether this node has no outgoing edges (i.e., is a leaf node).
    /// </summary>
    public bool IsTerminal => Edges.Count == 0;

    /// <summary>
    /// Constructs an agent graph node.
    /// </summary>
    public AgentGraphNode(string key, LdAiAgentConfig config, IReadOnlyList<GraphEdge> edges)
    {
        Key = key;
        Config = config;
        Edges = edges;
    }
}
