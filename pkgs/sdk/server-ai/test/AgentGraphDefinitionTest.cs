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
            modelKey: null,
            modelVersion: 1,
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

    // Test 32: Traverse visits nodes in topological order from root
    [Fact]
    public void TraverseVisitsNodesInTopologicalOrder()
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

    // Test 35: Cycle-safe — pure cycle visits all nodes, root last
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

        Assert.Equal(3, visited.Count);
        Assert.Equal("a", visited[visited.Count - 1]);
        Assert.Contains("a", visited);
        Assert.Contains("b", visited);
        Assert.Contains("c", visited);
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
        Assert.Equal("a", visited[0]);
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
    public void TraversePassesScopedResultsBetweenDependentNodes()
    {
        var graph = BuildEnabled(ThreeNodeFlagValue(), ThreeNodeConfigs());

        var seen = new Dictionary<string, Dictionary<string, object>>();
        graph.Traverse((node, context) =>
        {
            seen[node.Key] = new Dictionary<string, object>(context);
            return node.Key.ToUpper();
        });

        Assert.Empty(seen["agent-a"]);
        Assert.Equal("AGENT-A", seen["agent-b"]["agent-a"]);
        Assert.Equal("AGENT-A", seen["agent-c"]["agent-a"]);
        Assert.Equal("AGENT-B", seen["agent-c"]["agent-b"]);
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

    // -----------------------------------------------------------------------
    // Topological parity fixtures (G1–G6)
    // -----------------------------------------------------------------------

    private static AgentGraphDefinition BuildGraph(string root,
        Dictionary<string, IReadOnlyList<GraphEdge>> edges)
    {
        var keys = new HashSet<string> { root };
        foreach (var kv in edges)
        {
            keys.Add(kv.Key);
            foreach (var e in kv.Value) keys.Add(e.Key);
        }
        var configs = keys.ToDictionary(k => k, k => MakeAgentConfig(k));
        var flagValue = new AgentGraphFlagValue
        {
            Root = root,
            Edges = edges,
            Meta = new LdMeta { Enabled = true }
        };
        return BuildEnabled(flagValue, configs);
    }

    private static List<string> CollectOrder(AgentGraphDefinition graph, bool reverse)
    {
        var order = new List<string>();
        if (reverse)
            graph.ReverseTraverse((node, _) => { order.Add(node.Key); return null; });
        else
            graph.Traverse((node, _) => { order.Add(node.Key); return null; });
        return order;
    }

    private static void AssertExactContextKeys(
        Dictionary<string, object> ctx, IEnumerable<string> expected)
    {
        var actual = ctx.Keys.OrderBy(k => k).ToArray();
        var expectedSorted = expected.OrderBy(k => k).ToArray();
        Assert.Equal(expectedSorted, actual);
    }

    private static readonly Dictionary<string, string[]> G2ForwardContext =
        new Dictionary<string, string[]>
        {
            ["a"] = Array.Empty<string>(),
            ["b"] = new[] { "a" },
            ["c"] = new[] { "a" },
            ["d"] = new[] { "a", "c" },
            ["e"] = new[] { "a", "b", "c", "d" }
        };

    private static readonly Dictionary<string, string[]> G2ReverseContext =
        new Dictionary<string, string[]>
        {
            ["a"] = new[] { "b", "c", "d", "e" },
            ["b"] = new[] { "e" },
            ["c"] = new[] { "d", "e" },
            ["d"] = new[] { "e" },
            ["e"] = Array.Empty<string>()
        };

    /// <summary>
    /// Canonical G1–G6/G2b vectors from sdk-specs test-vectors/vectors.json:
    /// order plus exact traverse_context / reverse_traverse_context.
    /// </summary>
    public static IEnumerable<object[]> Vectors()
    {
        yield return new object[]
        {
            "G1",
            "a",
            new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null) },
                ["b"] = new[] { new GraphEdge("c", null) }
            },
            new[] { "a", "b", "c" },
            new[] { "c", "b", "a" },
            new Dictionary<string, string[]>
            {
                ["a"] = Array.Empty<string>(),
                ["b"] = new[] { "a" },
                ["c"] = new[] { "a", "b" }
            },
            new Dictionary<string, string[]>
            {
                ["a"] = new[] { "b", "c" },
                ["b"] = new[] { "c" },
                ["c"] = Array.Empty<string>()
            }
        };
        yield return new object[]
        {
            "G2",
            "a",
            new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
                ["b"] = new[] { new GraphEdge("e", null) },
                ["c"] = new[] { new GraphEdge("d", null) },
                ["d"] = new[] { new GraphEdge("e", null) }
            },
            new[] { "a", "b", "c", "d", "e" },
            new[] { "e", "b", "d", "c", "a" },
            G2ForwardContext,
            G2ReverseContext
        };
        yield return new object[]
        {
            "G2b",
            "a",
            new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("c", null), new GraphEdge("b", null) },
                ["b"] = new[] { new GraphEdge("e", null) },
                ["c"] = new[] { new GraphEdge("d", null) },
                ["d"] = new[] { new GraphEdge("e", null) }
            },
            new[] { "a", "c", "b", "d", "e" },
            new[] { "e", "b", "d", "c", "a" },
            G2ForwardContext,
            G2ReverseContext
        };
        yield return new object[]
        {
            "G3",
            "a",
            new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
                ["b"] = new[] { new GraphEdge("d", null) },
                ["c"] = new[] { new GraphEdge("d", null) }
            },
            new[] { "a", "b", "c", "d" },
            new[] { "d", "b", "c", "a" },
            new Dictionary<string, string[]>
            {
                ["a"] = Array.Empty<string>(),
                ["b"] = new[] { "a" },
                ["c"] = new[] { "a" },
                ["d"] = new[] { "a", "b", "c" }
            },
            new Dictionary<string, string[]>
            {
                ["a"] = new[] { "b", "c", "d" },
                ["b"] = new[] { "d" },
                ["c"] = new[] { "d" },
                ["d"] = Array.Empty<string>()
            }
        };
        yield return new object[]
        {
            "G4",
            "a",
            new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("n", null) },
                ["n"] = new[] { new GraphEdge("m", null), new GraphEdge("t", null) },
                ["m"] = new[] { new GraphEdge("t", null) }
            },
            new[] { "a", "n", "m", "t" },
            new[] { "t", "m", "n", "a" },
            new Dictionary<string, string[]>
            {
                ["a"] = Array.Empty<string>(),
                ["n"] = new[] { "a" },
                ["m"] = new[] { "a", "n" },
                ["t"] = new[] { "a", "m", "n" }
            },
            new Dictionary<string, string[]>
            {
                ["a"] = new[] { "m", "n", "t" },
                ["n"] = new[] { "m", "t" },
                ["m"] = new[] { "t" },
                ["t"] = Array.Empty<string>()
            }
        };
        yield return new object[]
        {
            "G5",
            "a",
            new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
                ["b"] = new[] { new GraphEdge("d", null) }
            },
            new[] { "a", "b", "c", "d" },
            new[] { "c", "d", "b", "a" },
            new Dictionary<string, string[]>
            {
                ["a"] = Array.Empty<string>(),
                ["b"] = new[] { "a" },
                ["c"] = new[] { "a" },
                ["d"] = new[] { "a", "b" }
            },
            new Dictionary<string, string[]>
            {
                ["a"] = new[] { "b", "c", "d" },
                ["b"] = new[] { "d" },
                ["c"] = Array.Empty<string>(),
                ["d"] = Array.Empty<string>()
            }
        };
        yield return new object[]
        {
            "G6",
            "a",
            new Dictionary<string, IReadOnlyList<GraphEdge>>
            {
                ["a"] = new[] { new GraphEdge("b", null) },
                ["b"] = new[] { new GraphEdge("c", null) },
                ["c"] = new[] { new GraphEdge("b", null) }
            },
            new[] { "a", "b", "c" },
            new[] { "b", "c", "a" },
            new Dictionary<string, string[]>
            {
                ["a"] = Array.Empty<string>(),
                ["b"] = new[] { "a" },
                ["c"] = new[] { "a", "b" }
            },
            new Dictionary<string, string[]>
            {
                ["a"] = new[] { "b", "c" },
                ["b"] = Array.Empty<string>(),
                ["c"] = new[] { "b" }
            }
        };
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void VectorTraversalOrderAndContext(
        string id,
        string root,
        Dictionary<string, IReadOnlyList<GraphEdge>> edges,
        string[] traverse,
        string[] reverseTraverse,
        Dictionary<string, string[]> forwardContext,
        Dictionary<string, string[]> reverseContext)
    {
        Assert.NotNull(id);
        var graph = BuildGraph(root, edges);

        Assert.Equal(traverse, CollectOrder(graph, reverse: false));
        Assert.Equal(reverseTraverse, CollectOrder(graph, reverse: true));

        graph.Traverse((node, ctx) =>
        {
            AssertExactContextKeys(ctx, forwardContext[node.Key]);
            return $"result-of-{node.Key}";
        });

        graph.ReverseTraverse((node, ctx) =>
        {
            AssertExactContextKeys(ctx, reverseContext[node.Key]);
            return $"result-of-{node.Key}";
        });
    }

    [Fact]
    public void G1_TraverseVisitsLinearChain()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null) },
            ["b"] = new[] { new GraphEdge("c", null) }
        });
        Assert.Equal(new[] { "a", "b", "c" }, CollectOrder(graph, reverse: false));
    }

    [Fact]
    public void G1_ReverseTraverseVisitsLinearChain()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null) },
            ["b"] = new[] { new GraphEdge("c", null) }
        });
        Assert.Equal(new[] { "c", "b", "a" }, CollectOrder(graph, reverse: true));
    }

    [Fact]
    public void G2_TraverseVisitsSkewedDiamond()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
            ["b"] = new[] { new GraphEdge("e", null) },
            ["c"] = new[] { new GraphEdge("d", null) },
            ["d"] = new[] { new GraphEdge("e", null) }
        });
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, CollectOrder(graph, reverse: false));
    }

    [Fact]
    public void G2_ReverseTraverseVisitsSkewedDiamond()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
            ["b"] = new[] { new GraphEdge("e", null) },
            ["c"] = new[] { new GraphEdge("d", null) },
            ["d"] = new[] { new GraphEdge("e", null) }
        });
        Assert.Equal(new[] { "e", "b", "d", "c", "a" }, CollectOrder(graph, reverse: true));
    }

    [Fact]
    public void G2b_TraverseKeepsELastWhenAEdgesReordered()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("c", null), new GraphEdge("b", null) },
            ["b"] = new[] { new GraphEdge("e", null) },
            ["c"] = new[] { new GraphEdge("d", null) },
            ["d"] = new[] { new GraphEdge("e", null) }
        });
        var order = CollectOrder(graph, reverse: false);
        Assert.Equal(new[] { "a", "c", "b", "d", "e" }, order);
        Assert.True(order.IndexOf("d") < order.IndexOf("e"));
    }

    [Fact]
    public void G2b_ReverseTraverseKeepsEBeforeDWhenAEdgesReordered()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("c", null), new GraphEdge("b", null) },
            ["b"] = new[] { new GraphEdge("e", null) },
            ["c"] = new[] { new GraphEdge("d", null) },
            ["d"] = new[] { new GraphEdge("e", null) }
        });
        var order = CollectOrder(graph, reverse: true);
        Assert.Equal("e", order[0]);
        Assert.True(order.IndexOf("e") < order.IndexOf("d"));
        Assert.True(order.IndexOf("e") < order.IndexOf("b"));
        Assert.Equal("a", order[order.Count - 1]);
    }

    [Fact]
    public void G3_TraverseVisitsSymmetricDiamond()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
            ["b"] = new[] { new GraphEdge("d", null) },
            ["c"] = new[] { new GraphEdge("d", null) }
        });
        Assert.Equal(new[] { "a", "b", "c", "d" }, CollectOrder(graph, reverse: false));
    }

    [Fact]
    public void G3_ReverseTraverseVisitsSymmetricDiamond()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
            ["b"] = new[] { new GraphEdge("d", null) },
            ["c"] = new[] { new GraphEdge("d", null) }
        });
        Assert.Equal(new[] { "d", "b", "c", "a" }, CollectOrder(graph, reverse: true));
    }

    [Fact]
    public void G4_TraverseVisitsNestedParent()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("n", null) },
            ["n"] = new[] { new GraphEdge("m", null), new GraphEdge("t", null) },
            ["m"] = new[] { new GraphEdge("t", null) }
        });
        Assert.Equal(new[] { "a", "n", "m", "t" }, CollectOrder(graph, reverse: false));
    }

    [Fact]
    public void G4_ReverseTraverseVisitsNestedParent()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("n", null) },
            ["n"] = new[] { new GraphEdge("m", null), new GraphEdge("t", null) },
            ["m"] = new[] { new GraphEdge("t", null) }
        });
        Assert.Equal(new[] { "t", "m", "n", "a" }, CollectOrder(graph, reverse: true));
    }

    [Fact]
    public void G5_TraverseVisitsMultiTerminal()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
            ["b"] = new[] { new GraphEdge("d", null) }
        });
        Assert.Equal(new[] { "a", "b", "c", "d" }, CollectOrder(graph, reverse: false));
    }

    [Fact]
    public void G5_ReverseTraverseVisitsMultiTerminal()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
            ["b"] = new[] { new GraphEdge("d", null) }
        });
        Assert.Equal(new[] { "c", "d", "b", "a" }, CollectOrder(graph, reverse: true));
    }

    [Fact]
    public void G6_TraverseVisitsCycleDeterministically()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null) },
            ["b"] = new[] { new GraphEdge("c", null) },
            ["c"] = new[] { new GraphEdge("b", null) }
        });
        var first = CollectOrder(graph, reverse: false);
        var second = CollectOrder(graph, reverse: false);
        Assert.Equal("a", first[0]);
        Assert.Equal(3, first.Count);
        Assert.Equal(new[] { "a", "b", "c" }, first.OrderBy(k => k).ToArray());
        Assert.Equal(first, second);
    }

    [Fact]
    public void G6_ReverseTraverseVisitsCycleDeterministically()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null) },
            ["b"] = new[] { new GraphEdge("c", null) },
            ["c"] = new[] { new GraphEdge("b", null) }
        });
        var first = CollectOrder(graph, reverse: true);
        var second = CollectOrder(graph, reverse: true);
        Assert.Equal("a", first[first.Count - 1]);
        Assert.Equal(3, first.Count);
        Assert.Equal(new[] { "a", "b", "c" }, first.OrderBy(k => k).ToArray());
        Assert.Equal(first, second);
        Assert.Equal(new[] { "b", "c", "a" }, first);
    }

    [Fact]
    public void TraverseIncludesInitialContextAlongsideScopedPredecessors()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
            ["b"] = new[] { new GraphEdge("e", null) },
            ["c"] = new[] { new GraphEdge("d", null) },
            ["d"] = new[] { new GraphEdge("e", null) }
        });

        var expectedDeps = new Dictionary<string, string[]>
        {
            ["a"] = Array.Empty<string>(),
            ["b"] = new[] { "a" },
            ["c"] = new[] { "a" },
            ["d"] = new[] { "a", "c" },
            ["e"] = new[] { "a", "b", "c", "d" }
        };

        graph.Traverse((node, ctx) =>
        {
            var expectedKeys = new List<string>(expectedDeps[node.Key]) { "seed" };
            AssertExactContextKeys(ctx, expectedKeys);
            Assert.Equal("value", ctx["seed"]);
            return $"result-of-{node.Key}";
        }, new Dictionary<string, object> { ["seed"] = "value" });
    }

    [Fact]
    public void TraverseVisitsSelfLoopOnce()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("a", null) }
        });
        Assert.Equal(new[] { "a" }, CollectOrder(graph, reverse: false));
    }

    [Fact]
    public void SelfLoopIsNotIncludedInOwnContext()
    {
        // a → b → b (self-loop on b)
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null) },
            ["b"] = new[] { new GraphEdge("b", null) }
        });
        var initial = new Dictionary<string, object> { ["seed"] = 1 };

        graph.Traverse((node, ctx) =>
        {
            if (node.Key == "b")
                AssertExactContextKeys(ctx, new[] { "seed", "a" });
            return $"result-of-{node.Key}";
        }, initial);

        graph.ReverseTraverse((node, ctx) =>
        {
            if (node.Key == "b")
                AssertExactContextKeys(ctx, new[] { "seed" });
            if (node.Key == "a")
                AssertExactContextKeys(ctx, new[] { "seed", "b" });
            return $"result-of-{node.Key}";
        }, initial);
    }

    [Fact]
    public void TraverseAndReverseTraverseAreDeterministic()
    {
        var graph = BuildGraph("a", new Dictionary<string, IReadOnlyList<GraphEdge>>
        {
            ["a"] = new[] { new GraphEdge("b", null), new GraphEdge("c", null) },
            ["b"] = new[] { new GraphEdge("e", null) },
            ["c"] = new[] { new GraphEdge("d", null) },
            ["d"] = new[] { new GraphEdge("e", null) }
        });
        Assert.Equal(CollectOrder(graph, reverse: false), CollectOrder(graph, reverse: false));
        Assert.Equal(CollectOrder(graph, reverse: true), CollectOrder(graph, reverse: true));
    }
}
