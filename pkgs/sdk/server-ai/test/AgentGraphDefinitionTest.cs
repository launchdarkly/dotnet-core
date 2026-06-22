using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Graph;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// Tests for AgentGraphDefinition (spec tests 26–35).
/// </summary>
public class AgentGraphDefinitionTest
{
    private static LdAiAgentConfig MakeAgentConfig(string key, bool enabled = true)
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(new Mock<ILogger>().Object);
        return new LdAiAgentConfig(
            key: key,
            enabled: enabled,
            variationKey: "v1",
            version: 1,
            instructions: null,
            tools: new Dictionary<string, LdAiConfigTypes.Tool>(),
            model: null,
            provider: null,
            judgeConfiguration: null,
            trackerFactory: _ => new LdAiConfigTracker(mockClient.Object, Guid.NewGuid().ToString(),
                key, "v1", 1, Context.New("u"), "", ""));
    }

    private static AgentGraphFlagValue ThreeNodeFlagValue()
    {
        return new AgentGraphFlagValue
        {
            Root = "agent-a",
            Edges = new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["agent-a"] = new[] { new GraphEdge("agent-b", null) },
                ["agent-b"] = new[] { new GraphEdge("agent-c", null) }
            },
            Meta = new LdMeta { VariationKey = "v1", Version = 1, Enabled = true }
        };
    }

    private static IReadOnlyDictionary<string, LdAiAgentConfig> ThreeNodeConfigs()
    {
        return new Dictionary<string, LdAiAgentConfig>
        {
            ["agent-a"] = MakeAgentConfig("agent-a"),
            ["agent-b"] = MakeAgentConfig("agent-b"),
            ["agent-c"] = MakeAgentConfig("agent-c")
        };
    }

    private static AgentGraphDefinition BuildEnabled(AgentGraphFlagValue flagValue,
        IReadOnlyDictionary<string, LdAiAgentConfig> configs)
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(new Mock<ILogger>().Object);
        var nodes = AgentGraphDefinition.BuildNodes(flagValue, configs);
        return new AgentGraphDefinition(flagValue, nodes, enabled: true,
            createTracker: () => new AiGraphTracker(mockClient.Object, "g", 1, Context.New("u")));
    }

    // Test 26: BuildNodes populates each node with structured GraphEdge[] from flag value edges map
    [Fact]
    public void BuildNodesPopulatesEdgesFromFlagValue()
    {
        var flagValue = ThreeNodeFlagValue();
        var configs = ThreeNodeConfigs();

        var nodes = AgentGraphDefinition.BuildNodes(flagValue, configs);

        Assert.Equal(3, nodes.Count);
        Assert.True(nodes.ContainsKey("agent-a"));
        Assert.True(nodes.ContainsKey("agent-b"));
        Assert.True(nodes.ContainsKey("agent-c"));

        Assert.Single(nodes["agent-a"].Edges);
        Assert.Equal("agent-b", nodes["agent-a"].Edges[0].Key);

        Assert.Single(nodes["agent-b"].Edges);
        Assert.Equal("agent-c", nodes["agent-b"].Edges[0].Key);

        Assert.Empty(nodes["agent-c"].Edges);
    }

    // Test 27: GetChildNodes maps through node's Edges (edge.Key → node lookup)
    [Fact]
    public void GetChildNodesMapsEdgesToNodes()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());

        var childrenOfA = graph.GetChildNodes("agent-a");
        Assert.Single(childrenOfA);
        Assert.Equal("agent-b", childrenOfA[0].Key);

        var childrenOfB = graph.GetChildNodes("agent-b");
        Assert.Single(childrenOfB);
        Assert.Equal("agent-c", childrenOfB[0].Key);

        var childrenOfC = graph.GetChildNodes("agent-c");
        Assert.Empty(childrenOfC);
    }

    // Test 28: GetParentNodes finds all nodes whose edges reference the given key
    [Fact]
    public void GetParentNodesFindsByEdgeTarget()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());

        var parentsOfA = graph.GetParentNodes("agent-a");
        Assert.Empty(parentsOfA);

        var parentsOfB = graph.GetParentNodes("agent-b");
        Assert.Single(parentsOfB);
        Assert.Equal("agent-a", parentsOfB[0].Key);

        var parentsOfC = graph.GetParentNodes("agent-c");
        Assert.Single(parentsOfC);
        Assert.Equal("agent-b", parentsOfC[0].Key);
    }

    // Test 29: TerminalNodes returns nodes with no outgoing edges
    [Fact]
    public void TerminalNodesReturnsLeafNodes()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());

        var terminals = graph.TerminalNodes();
        Assert.Single(terminals);
        Assert.Equal("agent-c", terminals[0].Key);
    }

    // Test 30: RootNode returns node matching GetConfig().Root
    [Fact]
    public void RootNodeReturnsNodeMatchingRoot()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());

        var root = graph.RootNode();
        Assert.NotNull(root);
        Assert.Equal("agent-a", root.Key);
        Assert.Equal(graph.GetConfig().Root, root.Key);
    }

    // Test 31: GetConfig returns AgentGraphFlagValue with Meta nested (variationKey, version, enabled)
    [Fact]
    public void GetConfigReturnsFlagValueWithMeta()
    {
        var flagValue = ThreeNodeFlagValue();
        var graph = BuildEnabled(flagValue, ThreeNodeConfigs());

        var config = graph.GetConfig();
        Assert.Equal("agent-a", config.Root);
        Assert.NotNull(config.Meta);
        Assert.Equal("v1", config.Meta.VariationKey);
        Assert.Equal(1, config.Meta.Version);
        Assert.True(config.Meta.Enabled);
    }

    // Test 31b: GetConfig is still available when graph is disabled
    [Fact]
    public void GetConfigAvailableOnDisabledGraph()
    {
        var flagValue = ThreeNodeFlagValue();
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(new Mock<ILogger>().Object);
        var disabled = new AgentGraphDefinition(flagValue, new Dictionary<string, AgentGraphNode>(),
            enabled: false, createTracker: () => new AiGraphTracker(mockClient.Object, "g", 1, Context.New("u")));

        Assert.False(disabled.Enabled);
        Assert.Same(flagValue, disabled.GetConfig());
    }

    // Test 32: Traverse visits nodes BFS from root
    [Fact]
    public void TraverseVisitsNodesInBfsOrder()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());

        var visited = new List<string>();
        graph.Traverse((node, ctx) =>
        {
            visited.Add(node.Key);
            return null;
        });

        Assert.Equal(new[] { "agent-a", "agent-b", "agent-c" }, visited);
    }

    // Test 33: ReverseTraverse visits from terminals upward, root always last
    [Fact]
    public void ReverseTraverseVisitsRootLast()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());

        var visited = new List<string>();
        graph.ReverseTraverse((node, ctx) =>
        {
            visited.Add(node.Key);
            return null;
        });

        Assert.Equal(3, visited.Count);
        Assert.Equal("agent-c", visited[0]);
        Assert.Equal("agent-a", visited[visited.Count - 1]);
    }

    // Test 34: CreateTracker returns AiGraphTracker with correct graphKey
    [Fact]
    public void CreateTrackerReturnsGraphTrackerWithGraphKey()
    {
        var flagValue = ThreeNodeFlagValue();
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(new Mock<ILogger>().Object);
        var context = Context.New("user");
        var nodes = AgentGraphDefinition.BuildNodes(flagValue, ThreeNodeConfigs());
        var graph = new AgentGraphDefinition(flagValue, nodes, enabled: true,
            createTracker: () => new AiGraphTracker(mockClient.Object, "my-graph-key", 1, context));

        var tracker = graph.CreateTracker();
        Assert.Equal("my-graph-key", tracker.GetTrackData().GraphKey);
    }

    // Test 35: Cycle-safe — pure cycle has no terminal nodes, so reverse traversal is a no-op
    [Fact]
    public void ReverseTraverseIsCycleSafe()
    {
        // a → b → c → a (pure cycle, no terminal nodes)
        var flagValue = new AgentGraphFlagValue
        {
            Root = "a",
            Edges = new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null) },
                ["b"] = new[] { new GraphEdge("c", null) },
                ["c"] = new[] { new GraphEdge("a", null) }
            },
            Meta = new LdMeta { Enabled = true }
        };
        var configs = new Dictionary<string, LdAiAgentConfig>
        {
            ["a"] = MakeAgentConfig("a"),
            ["b"] = MakeAgentConfig("b"),
            ["c"] = MakeAgentConfig("c")
        };

        var graph = BuildEnabled(flagValue, configs);

        var visited = new List<string>();
        graph.ReverseTraverse((node, ctx) =>
        {
            visited.Add(node.Key);
            return null;
        });

        // Pure cycle has no terminal nodes — reverse traversal is a no-op per spec AIGRAPH 1.4
        Assert.Empty(visited);
    }

    // Test 36: Cycle-safe — graph with cycles doesn't infinite loop
    [Fact]
    public void TraverseIsCycleSafe()
    {
        // a → b → c → a (cycle)
        var flagValue = new AgentGraphFlagValue
        {
            Root = "a",
            Edges = new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null) },
                ["b"] = new[] { new GraphEdge("c", null) },
                ["c"] = new[] { new GraphEdge("a", null) }
            },
            Meta = new LdMeta { Enabled = true }
        };
        var configs = new Dictionary<string, LdAiAgentConfig>
        {
            ["a"] = MakeAgentConfig("a"),
            ["b"] = MakeAgentConfig("b"),
            ["c"] = MakeAgentConfig("c")
        };

        var graph = BuildEnabled(flagValue, configs);

        var visited = new List<string>();
        graph.Traverse((node, ctx) =>
        {
            visited.Add(node.Key);
            return null;
        });

        // Each node visited exactly once despite cycle
        Assert.Equal(3, visited.Count);
        Assert.Contains("a", visited);
        Assert.Contains("b", visited);
        Assert.Contains("c", visited);
    }

    [Fact]
    public void GetNodeReturnsNullForUnknownKey()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());
        Assert.Null(graph.GetNode("nonexistent"));
    }

    [Fact]
    public void GetChildNodesReturnsEmptyForUnknownNode()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());
        Assert.Empty(graph.GetChildNodes("nonexistent"));
    }

    [Fact]
    public void CollectAllKeysIncludesRootEdgeSourcesAndTargets()
    {
        var flagValue = new AgentGraphFlagValue
        {
            Root = "a",
            Edges = new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) }
            }
        };

        var keys = AgentGraphDefinition.CollectAllKeys(flagValue);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Contains("c", keys);
        Assert.Equal(3, keys.Count);
    }

    [Fact]
    public void BuildNodesSkipsKeysMissingFromConfigs()
    {
        var flagValue = new AgentGraphFlagValue
        {
            Root = "a",
            Edges = new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null) }
            }
        };
        // Only provide config for "a", not "b"
        var configs = new Dictionary<string, LdAiAgentConfig>
        {
            ["a"] = MakeAgentConfig("a")
        };

        var nodes = AgentGraphDefinition.BuildNodes(flagValue, configs);
        Assert.Single(nodes);
        Assert.True(nodes.ContainsKey("a"));
        Assert.False(nodes.ContainsKey("b"));
    }

    [Fact]
    public void TraversePassesContextBetweenNodes()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());

        var ctx = new Dictionary<string, object>();
        graph.Traverse((node, context) =>
        {
            context[$"{node.Key}-visited"] = true;
            return node.Key.ToUpper();
        }, ctx);

        Assert.Equal("AGENT-A", ctx["agent-a"]);
        Assert.Equal("AGENT-B", ctx["agent-b"]);
        Assert.Equal("AGENT-C", ctx["agent-c"]);
    }

    [Fact]
    public void IsTerminalTrueForNodeWithNoEdges()
    {
        var flagValue = ThreeNodeFlagValue();
        var nodes = AgentGraphDefinition.BuildNodes(flagValue, ThreeNodeConfigs());

        Assert.False(nodes["agent-a"].IsTerminal);
        Assert.False(nodes["agent-b"].IsTerminal);
        Assert.True(nodes["agent-c"].IsTerminal);
    }

    [Fact]
    public void GraphEdgeHandoffDataPreserved()
    {
        var flagValue = new AgentGraphFlagValue
        {
            Root = "a",
            Edges = new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[]
                {
                    new GraphEdge("b", new Dictionary<string, LdValue> { ["tool"] = LdValue.Of("search") })
                }
            }
        };
        var configs = new Dictionary<string, LdAiAgentConfig>
        {
            ["a"] = MakeAgentConfig("a"),
            ["b"] = MakeAgentConfig("b")
        };

        var nodes = AgentGraphDefinition.BuildNodes(flagValue, configs);
        var edge = nodes["a"].Edges[0];
        Assert.Equal("b", edge.Key);
        Assert.NotNull(edge.Handoff);
        Assert.Equal("search", edge.Handoff["tool"].AsString);
    }

    [Fact]
    public void GraphEdgeWithNoHandoffHasNullHandoff()
    {
        var flagValue = ThreeNodeFlagValue();
        var nodes = AgentGraphDefinition.BuildNodes(flagValue, ThreeNodeConfigs());
        Assert.Null(nodes["agent-a"].Edges[0].Handoff);
    }
}
