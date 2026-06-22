using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Graph;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// Tests for LdAiClient.AgentGraph() and CreateGraphTracker() (spec tests 36–44).
/// </summary>
public class LdAiClientAgentGraphTest
{
    private static (LdAiClient client, Mock<ILaunchDarklyClient> mockClient) MakeClient()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(new Mock<ILogger>().Object);
        return (new LdAiClient(mockClient.Object), mockClient);
    }

    /// <summary>
    /// Returns an LdValue representing a valid agent config flag variation for the given key.
    /// Mode must be "agent" to pass ConfigFactory's mode check.
    /// </summary>
    private static LdValue AgentConfigValue(string variationKey = "v1", bool enabled = true)
    {
        return LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["enabled"] = LdValue.Of(enabled),
                ["variationKey"] = LdValue.Of(variationKey),
                ["version"] = LdValue.Of(1),
                ["mode"] = LdValue.Of("agent")
            }),
            ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["name"] = LdValue.Of("gpt-4")
            }),
            ["provider"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["name"] = LdValue.Of("openai")
            }),
            ["instructions"] = LdValue.Of("You are helpful.")
        });
    }

    /// <summary>
    /// Returns an LdValue for a two-node graph flag: root=agent-a, edge a→b.
    /// </summary>
    private static LdValue TwoNodeGraphValue(string variationKey = "gv1", bool metaEnabled = true)
    {
        return LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            ["root"] = LdValue.Of("agent-a"),
            ["edges"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["agent-a"] = LdValue.ArrayOf(
                    LdValue.ObjectFrom(new Dictionary<string, LdValue>
                    {
                        ["key"] = LdValue.Of("agent-b")
                    }))
            }),
            ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["enabled"] = LdValue.Of(metaEnabled),
                ["variationKey"] = LdValue.Of(variationKey),
                ["version"] = LdValue.Of(2)
            })
        });
    }

    private static void SetupAgentConfigReturns(Mock<ILaunchDarklyClient> mockClient,
        params string[] nodeKeys)
    {
        foreach (var key in nodeKeys)
        {
            var capturedKey = key;
            mockClient.Setup(c => c.JsonVariation(capturedKey, It.IsAny<Context>(), It.IsAny<LdValue>()))
                .Returns(AgentConfigValue());
        }
    }

    // Test 36: Valid graph → returns enabled definition with structured nodes/edges
    [Fact]
    public void ValidGraphReturnsEnabledDefinition()
    {
        var (client, mockClient) = MakeClient();
        var context = Context.New("user");

        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(TwoNodeGraphValue());
        SetupAgentConfigReturns(mockClient, "agent-a", "agent-b");

        var result = client.AgentGraph("my-graph", context);

        Assert.True(result.Enabled);
        Assert.NotNull(result.RootNode());
        Assert.Equal("agent-a", result.RootNode().Key);
        Assert.NotNull(result.GetNode("agent-b"));
    }

    // Test 37: _ldMeta.enabled === false → AgentGraphDefinition.Enabled = false + debug log
    [Fact]
    public void MetaDisabledReturnsFalseEnabledWithDebugLog()
    {
        var mockLogger = new Mock<ILogger>();
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(mockLogger.Object);
        var context = Context.New("user");

        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(TwoNodeGraphValue(metaEnabled: false));

        var client = new LdAiClient(mockClient.Object);
        var result = client.AgentGraph("my-graph", context);

        Assert.False(result.Enabled);
        mockLogger.Verify(l => l.Debug(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeastOnce);
    }

    // Test 38: _ldMeta absent → defaults to enabled (no false check fires)
    [Fact]
    public void MissingMetaDefaultsToEnabled()
    {
        var (client, mockClient) = MakeClient();
        var context = Context.New("user");

        // Graph flag with no _ldMeta
        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["root"] = LdValue.Of("agent-a"),
                ["edges"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["agent-a"] = LdValue.ArrayOf(
                        LdValue.ObjectFrom(new Dictionary<string, LdValue>
                        {
                            ["key"] = LdValue.Of("agent-b")
                        }))
                })
            }));
        SetupAgentConfigReturns(mockClient, "agent-a", "agent-b");

        var result = client.AgentGraph("my-graph", context);

        Assert.True(result.Enabled);
    }

    // Test 39: Missing root → disabled + debug log
    [Fact]
    public void MissingRootReturnsDisabledWithDebugLog()
    {
        var mockLogger = new Mock<ILogger>();
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(mockLogger.Object);
        var context = Context.New("user");

        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns((string _, Context _, LdValue dv) => dv); // returns default {"root": ""}

        var client = new LdAiClient(mockClient.Object);
        var result = client.AgentGraph("my-graph", context);

        Assert.False(result.Enabled);
        mockLogger.Verify(l => l.Debug(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeastOnce);
    }

    // Test 40: Unconnected node → disabled + debug log
    [Fact]
    public void UnconnectedNodeReturnsDisabledWithDebugLog()
    {
        var mockLogger = new Mock<ILogger>();
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(mockLogger.Object);
        var context = Context.New("user");

        // a → b, but c is listed as edge source from nowhere (disconnected island)
        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["root"] = LdValue.Of("agent-a"),
                ["edges"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["agent-a"] = LdValue.ArrayOf(
                        LdValue.ObjectFrom(new Dictionary<string, LdValue>
                        {
                            ["key"] = LdValue.Of("agent-b")
                        })),
                    // agent-c is an edge source not reachable from root
                    ["agent-c"] = LdValue.ArrayOf(
                        LdValue.ObjectFrom(new Dictionary<string, LdValue>
                        {
                            ["key"] = LdValue.Of("agent-d")
                        }))
                }),
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["version"] = LdValue.Of(1)
                })
            }));

        var client = new LdAiClient(mockClient.Object);
        var result = client.AgentGraph("my-graph", context);

        Assert.False(result.Enabled);
        mockLogger.Verify(l => l.Debug(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeastOnce);
    }

    // Test 41: Child config disabled → disabled + debug log
    [Fact]
    public void DisabledChildConfigReturnsDisabledWithDebugLog()
    {
        var mockLogger = new Mock<ILogger>();
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(mockLogger.Object);
        var context = Context.New("user");

        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(TwoNodeGraphValue());
        mockClient.Setup(c => c.JsonVariation("agent-a", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(AgentConfigValue(enabled: true));
        mockClient.Setup(c => c.JsonVariation("agent-b", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(AgentConfigValue(enabled: false));

        var client = new LdAiClient(mockClient.Object);
        var result = client.AgentGraph("my-graph", context);

        Assert.False(result.Enabled);
        mockLogger.Verify(l => l.Debug(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeastOnce);
    }

    // Test 42: Per-node trackers include graphKey (via BuildAgentConfig threading)
    [Fact]
    public void PerNodeTrackersIncludeGraphKey()
    {
        var (client, mockClient) = MakeClient();
        var context = Context.New("user");

        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(TwoNodeGraphValue());
        SetupAgentConfigReturns(mockClient, "agent-a", "agent-b");

        var result = client.AgentGraph("my-graph", context);

        Assert.True(result.Enabled);

        // Verify that per-node tracker emits graphKey in track data
        var nodeTracker = result.GetNode("agent-a").Config.CreateTracker();
        nodeTracker.TrackSuccess();

        mockClient.Verify(c => c.Track(
            "$ld:ai:generation:success",
            context,
            It.Is<LdValue>(v => v.Get("graphKey").AsString == "my-graph"),
            It.IsAny<double>()), Times.Once);
    }

    // Test 43: Disabled definition still has GetConfig() returning the raw flag value
    [Fact]
    public void DisabledGraphDefinitionStillExposesGetConfig()
    {
        var (client, mockClient) = MakeClient();
        var context = Context.New("user");

        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(TwoNodeGraphValue(variationKey: "var-123", metaEnabled: false));

        var result = client.AgentGraph("my-graph", context);

        Assert.False(result.Enabled);
        var config = result.GetConfig();
        Assert.NotNull(config);
        Assert.Equal("agent-a", config.Root);
        Assert.NotNull(config.Meta);
        Assert.Equal("var-123", config.Meta.VariationKey);
    }

    // Test 44: CreateGraphTracker delegates to AiGraphTracker.FromResumptionToken; returned tracker has correct graphKey/runId
    [Fact]
    public void CreateGraphTrackerDelegatesToFromResumptionToken()
    {
        var (client, mockClient) = MakeClient();
        var context = Context.New("user");

        var runId = Guid.NewGuid().ToString();
        var original = new AiGraphTracker(mockClient.Object, "test-graph", 3, context, "vkey", runId);
        var token = original.ResumptionToken;

        var reconstructed = client.CreateGraphTracker(token, context);

        Assert.Equal(runId, reconstructed.GetTrackData().RunId);
        Assert.Equal("test-graph", reconstructed.GetTrackData().GraphKey);
        Assert.Equal("vkey", reconstructed.GetTrackData().VariationKey);
        Assert.Equal(3, reconstructed.GetTrackData().Version);
    }

    // Test: AgentGraph fires $ld:ai:usage:agent-graph tracking event
    [Fact]
    public void AgentGraphFiresUsageTrackingEvent()
    {
        var (client, mockClient) = MakeClient();
        var context = Context.New("user");

        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(TwoNodeGraphValue());
        SetupAgentConfigReturns(mockClient, "agent-a", "agent-b");

        client.AgentGraph("my-graph", context);

        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:agent-graph",
            context,
            LdValue.Of("my-graph"),
            1), Times.Once);
    }

    // Test: Edges with handoff data are parsed correctly
    [Fact]
    public void EdgeHandoffDataParsedCorrectly()
    {
        var (client, mockClient) = MakeClient();
        var context = Context.New("user");

        mockClient.Setup(c => c.JsonVariation("my-graph", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["root"] = LdValue.Of("agent-a"),
                ["edges"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["agent-a"] = LdValue.ArrayOf(
                        LdValue.ObjectFrom(new Dictionary<string, LdValue>
                        {
                            ["key"] = LdValue.Of("agent-b"),
                            ["handoff"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                            {
                                ["tool"] = LdValue.Of("search")
                            })
                        }))
                }),
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["version"] = LdValue.Of(1)
                })
            }));
        SetupAgentConfigReturns(mockClient, "agent-a", "agent-b");

        var result = client.AgentGraph("my-graph", context);

        Assert.True(result.Enabled);
        var edgesFromA = result.GetNode("agent-a").Edges;
        Assert.Single(edgesFromA);
        Assert.Equal("agent-b", edgesFromA[0].Key);
        Assert.NotNull(edgesFromA[0].Handoff);
        Assert.Equal("search", edgesFromA[0].Handoff["tool"].AsString);
    }
}
