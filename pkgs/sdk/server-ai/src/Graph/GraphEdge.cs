using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Graph;

/// <summary>
/// A directed edge in an agent graph, connecting a source node to a target node.
/// The source is implicit — it is the node that owns this edge.
/// </summary>
public sealed record GraphEdge(
    string Key,
    IReadOnlyDictionary<string, LdValue> Handoff
);
