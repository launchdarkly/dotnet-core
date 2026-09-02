using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.Config;

namespace LaunchDarkly.Sdk.Server.Ai.Graph;

/// <summary>
/// Represents a fully-resolved agent graph returned by
/// <see cref="LdAiClient.AgentGraph"/>. When <see cref="Enabled"/> is false, all
/// node collections are empty and traversal is a no-op; only <see cref="GetConfig"/>
/// and <see cref="CreateTracker"/> remain meaningful.
/// </summary>
public sealed class AgentGraphDefinition
{
    private readonly AgentGraphFlagValue _flagValue;
    private readonly IReadOnlyDictionary<string, AgentGraphNode> _nodes;
    private readonly Func<AiGraphTracker> _createTracker;

    /// <summary>
    /// Whether the graph passed all validation checks. False if the flag's
    /// <c>_ldMeta.enabled</c> is false, the root is missing, any node is
    /// unreachable from the root, or any child agent config could not be fetched.
    /// </summary>
    public bool Enabled { get; }

    internal AgentGraphDefinition(
        AgentGraphFlagValue flagValue,
        IReadOnlyDictionary<string, AgentGraphNode> nodes,
        bool enabled,
        Func<AiGraphTracker> createTracker)
    {
        _flagValue = flagValue;
        _nodes = nodes;
        Enabled = enabled;
        _createTracker = createTracker;
    }

    /// <summary>
    /// Returns the root node of the graph, or null if the graph is disabled or has no root.
    /// </summary>
    public AgentGraphNode RootNode() =>
        string.IsNullOrEmpty(_flagValue?.Root) ? null : GetNode(_flagValue.Root);

    /// <summary>
    /// Returns the node with the given key, or null if not found.
    /// </summary>
    public AgentGraphNode GetNode(string nodeKey)
    {
        if (nodeKey == null) return null;
        return _nodes.TryGetValue(nodeKey, out var node) ? node : null;
    }

    /// <summary>
    /// Returns the direct children of the given node by following its outgoing edges.
    /// Returns an empty list if the node is not found.
    /// </summary>
    public IReadOnlyList<AgentGraphNode> GetChildNodes(string nodeKey)
    {
        var node = GetNode(nodeKey);
        if (node == null) return Array.Empty<AgentGraphNode>();

        return node.Edges
            .Select(edge => GetNode(edge.Key))
            .Where(n => n != null)
            .ToList();
    }

    /// <summary>
    /// Returns all nodes that have an outgoing edge pointing to the given node key.
    /// </summary>
    public IReadOnlyList<AgentGraphNode> GetParentNodes(string nodeKey)
    {
        return _nodes.Values
            .Where(node => node.Edges.Any(edge => edge.Key == nodeKey))
            .ToList();
    }

    /// <summary>
    /// Returns all nodes with no outgoing edges (leaf nodes).
    /// </summary>
    public IReadOnlyList<AgentGraphNode> TerminalNodes() =>
        _nodes.Values.Where(n => n.IsTerminal).ToList();

    /// <summary>
    /// Returns the raw flag value including LaunchDarkly metadata. Always non-null,
    /// even when <see cref="Enabled"/> is false.
    /// </summary>
    public AgentGraphFlagValue GetConfig() => _flagValue;

    /// <summary>
    /// Creates a new graph-level tracker for this invocation.
    /// </summary>
    public AiGraphTracker CreateTracker() => _createTracker();

    /// <summary>
    /// Visits each reachable node in topological order (predecessors first, root first).
    /// Ties break by discovery order. Cycle-safe.
    /// </summary>
    /// <remarks>
    /// <paramref name="fn"/> receives <paramref name="initialContext"/> plus results
    /// from that node's reachable predecessors only.
    /// </remarks>
    public void Traverse(
        Func<AgentGraphNode, Dictionary<string, object>, object> fn,
        Dictionary<string, object> initialContext = null)
    {
        var root = RootNode();
        if (root == null) return;

        var context = initialContext ?? new Dictionary<string, object>();
        var (reachable, order) = ReachableAndDiscovery(root.Key);

        var indeg = reachable.ToDictionary(k => k, _ => 0);
        foreach (var k in reachable)
        {
            foreach (var e in GetNode(k).Edges)
            {
                if (reachable.Contains(e.Key)) indeg[e.Key]++;
            }
        }
        indeg[root.Key] = 0;

        var visited = new HashSet<string>();
        var results = new Dictionary<string, object>();
        var ancestors = new Dictionary<string, HashSet<string>>();

        Dictionary<string, object> Scoped(HashSet<string> deps)
        {
            var c = new Dictionary<string, object>(context);
            foreach (var k in deps) c[k] = results[k];
            return c;
        }

        while (visited.Count < reachable.Count)
        {
            var next = order.FirstOrDefault(k => !visited.Contains(k) && indeg[k] == 0)
                       ?? order.Where(k => !visited.Contains(k)).OrderBy(k => indeg[k]).First();

            var anc = new HashSet<string>();
            foreach (var parent in GetParentNodes(next))
            {
                if (!visited.Contains(parent.Key)) continue;
                anc.Add(parent.Key);
                anc.UnionWith(ancestors[parent.Key]);
            }
            ancestors[next] = anc;
            visited.Add(next);

            results[next] = fn(GetNode(next), Scoped(anc));
            foreach (var e in GetNode(next).Edges)
            {
                if (reachable.Contains(e.Key)) indeg[e.Key]--;
            }
        }
    }

    /// <summary>
    /// Visits each reachable node in reverse topological order (descendants first,
    /// root last). Ties break by discovery order. Cycle-safe.
    /// </summary>
    /// <remarks>
    /// <paramref name="fn"/> receives <paramref name="initialContext"/> plus results
    /// from that node's reachable descendants only.
    /// </remarks>
    public void ReverseTraverse(
        Func<AgentGraphNode, Dictionary<string, object>, object> fn,
        Dictionary<string, object> initialContext = null)
    {
        var root = RootNode();
        if (root == null) return;

        var rootKey = root.Key;
        var context = initialContext ?? new Dictionary<string, object>();
        var (reachable, order) = ReachableAndDiscovery(rootKey);

        var outdeg = reachable.ToDictionary(
            k => k, k => GetNode(k).Edges.Count(e => reachable.Contains(e.Key)));

        var visited = new HashSet<string>();
        var results = new Dictionary<string, object>();
        var descendants = new Dictionary<string, HashSet<string>>();

        Dictionary<string, object> Scoped(HashSet<string> deps)
        {
            var c = new Dictionary<string, object>(context);
            foreach (var k in deps) c[k] = results[k];
            return c;
        }

        bool NonRootRemaining() => reachable.Any(k => k != rootKey && !visited.Contains(k));
        while (NonRootRemaining())
        {
            var next = order.FirstOrDefault(k => k != rootKey && !visited.Contains(k) && outdeg[k] == 0)
                       ?? order.Where(k => k != rootKey && !visited.Contains(k)).OrderBy(k => outdeg[k]).First();

            var desc = new HashSet<string>();
            foreach (var e in GetNode(next).Edges)
            {
                if (!reachable.Contains(e.Key) || !visited.Contains(e.Key)) continue;
                desc.Add(e.Key);
                desc.UnionWith(descendants[e.Key]);
            }
            descendants[next] = desc;
            visited.Add(next);

            results[next] = fn(GetNode(next), Scoped(desc));
            foreach (var parent in GetParentNodes(next))
            {
                if (parent.Key != rootKey && reachable.Contains(parent.Key))
                    outdeg[parent.Key]--;
            }
        }

        // Root last
        var rootDeps = new HashSet<string>(reachable.Where(k => k != rootKey));
        results[rootKey] = fn(root, Scoped(rootDeps));
    }

    /// <summary>
    /// Reachable nodes from <paramref name="rootKey"/> and BFS discovery order
    /// (declared edge order). Used as a topological tie-break.
    /// </summary>
    private (HashSet<string> reachable, List<string> order) ReachableAndDiscovery(string rootKey)
    {
        var reachable = new HashSet<string>();
        var order = new List<string>();
        var queue = new Queue<string>();
        reachable.Add(rootKey);
        order.Add(rootKey);
        queue.Enqueue(rootKey);

        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            var node = GetNode(key);
            if (node == null) continue;
            foreach (var edge in node.Edges)
            {
                if (GetNode(edge.Key) != null && reachable.Add(edge.Key))
                {
                    order.Add(edge.Key);
                    queue.Enqueue(edge.Key);
                }
            }
        }

        return (reachable, order);
    }

    /// <summary>
    /// Builds the nodes dictionary from a parsed flag value and a map of pre-fetched
    /// agent configs, associating each node with its outgoing edges from the flag value.
    /// </summary>
    internal static IReadOnlyDictionary<string, AgentGraphNode> BuildNodes(
        AgentGraphFlagValue flagValue,
        IReadOnlyDictionary<string, LdAiAgentConfig> agentConfigs)
    {
        var nodes = new Dictionary<string, AgentGraphNode>();
        var allKeys = CollectAllKeys(flagValue);

        foreach (var key in allKeys)
        {
            if (!agentConfigs.TryGetValue(key, out var config))
                continue;

            var outgoingEdges = flagValue.Edges != null && flagValue.Edges.TryGetValue(key, out var edges)
                ? edges
                : (IReadOnlyList<GraphEdge>)Array.Empty<GraphEdge>();

            nodes[key] = new AgentGraphNode(key, config, outgoingEdges);
        }

        return nodes;
    }

    /// <summary>
    /// Collects all unique node keys referenced in the flag value: the root, all
    /// edge source keys, and all edge target keys.
    /// </summary>
    internal static HashSet<string> CollectAllKeys(AgentGraphFlagValue flagValue)
    {
        var keys = new HashSet<string>();

        if (!string.IsNullOrEmpty(flagValue?.Root))
        {
            keys.Add(flagValue.Root);
        }

        if (flagValue?.Edges != null)
        {
            foreach (var kv in flagValue.Edges)
            {
                keys.Add(kv.Key);
                foreach (var edge in kv.Value)
                {
                    keys.Add(edge.Key);
                }
            }
        }

        return keys;
    }
}
