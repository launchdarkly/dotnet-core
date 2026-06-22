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
    /// Performs a breadth-first traversal of the graph starting from the root node.
    /// For each visited node, <paramref name="fn"/> is called with the node and the
    /// accumulated context dictionary. The return value of <paramref name="fn"/> is
    /// stored in the context under the node's key and passed to subsequent calls.
    /// Cycle-safe: each node is visited at most once.
    /// </summary>
    public void Traverse(
        Func<AgentGraphNode, Dictionary<string, object>, object> fn,
        Dictionary<string, object> initialContext = null)
    {
        var root = RootNode();
        if (root == null) return;

        var context = initialContext ?? new Dictionary<string, object>();

        var visited = new HashSet<string>();
        var queue = new Queue<AgentGraphNode>();
        queue.Enqueue(root);
        visited.Add(root.Key);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            var result = fn(node, context);
            context[node.Key] = result;

            foreach (var child in GetChildNodes(node.Key))
            {
                if (visited.Add(child.Key))
                {
                    queue.Enqueue(child);
                }
            }
        }
    }

    /// <summary>
    /// Performs a reverse breadth-first traversal: starts from terminal nodes and
    /// works upward toward the root. The root node is always processed last.
    /// Cycle-safe: each node is visited at most once.
    /// </summary>
    public void ReverseTraverse(
        Func<AgentGraphNode, Dictionary<string, object>, object> fn,
        Dictionary<string, object> initialContext = null)
    {
        if (_nodes.Count == 0) return;

        var context = initialContext ?? new Dictionary<string, object>();

        var root = RootNode();
        var visited = new HashSet<string>();
        var queue = new Queue<AgentGraphNode>();

        // Seed with terminal nodes (excluding root if it happens to be terminal and there are others)
        foreach (var terminal in TerminalNodes())
        {
            if (root != null && terminal.Key == root.Key && _nodes.Count > 1)
            {
                continue;
            }
            if (visited.Add(terminal.Key))
            {
                queue.Enqueue(terminal);
            }
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            // Defer root until the very end (unless it's the only node)
            if (root != null && node.Key == root.Key && _nodes.Count > 1)
            {
                continue;
            }

            var result = fn(node, context);
            context[node.Key] = result;

            foreach (var parent in GetParentNodes(node.Key))
            {
                if (root != null && parent.Key == root.Key)
                {
                    // Don't enqueue root yet — process it last
                    continue;
                }
                if (visited.Add(parent.Key))
                {
                    queue.Enqueue(parent);
                }
            }
        }

        // Process root last
        if (root != null && _nodes.Count > 1 && visited.Count > 0)
        {
            var result = fn(root, context);
            context[root.Key] = result;
        }
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
