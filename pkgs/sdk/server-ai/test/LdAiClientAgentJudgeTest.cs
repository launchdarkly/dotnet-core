using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class LdAiClientAgentJudgeTest
{
    private static (Mock<ILaunchDarklyClient> MockClient, Mock<ILogger> MockLogger, LdAiClient Client) MakeClient()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
        return (mockClient, mockLogger, new LdAiClient(mockClient.Object));
    }

    // ── AgentConfig ─────────────────────────────────────────────────────────

    [Fact]
    public void AgentConfig_BasicRetrieval_ReturnsCorrectFields()
    {
        var (mockClient, _, client) = MakeClient();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {"name": "gpt-4o"},
                              "provider": {"name": "openai"},
                              "instructions": "Be helpful",
                              "tools": {
                                "search": {
                                  "name": "search",
                                  "description": "Web search",
                                  "type": "function",
                                  "parameters": {},
                                  "customParameters": {}
                                }
                              }
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("agent-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.AgentConfig("agent-key", Context.New("user"));

        Assert.Equal("agent-key", result.Key);
        Assert.True(result.Enabled);
        Assert.Equal("v1", result.VariationKey);
        Assert.Equal("Be helpful", result.Instructions);
        Assert.Equal("gpt-4o", result.Model.Name);
        Assert.Equal("openai", result.Provider.Name);
        Assert.Single(result.Tools);
        Assert.True(result.Tools.ContainsKey("search"));
        Assert.NotNull(result.CreateTracker());
    }

    [Fact]
    public void AgentConfig_ModeMismatch_ReturnsCallerDefault()
    {
        var (mockClient, mockLogger, client) = MakeClient();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "completion"},
                              "model": {"name": "ignored"},
                              "instructions": "ignored"
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("agent-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var defaultConfig = LdAiAgentConfigDefault.New().SetInstructions("fallback").Build();
        var result = client.AgentConfig("agent-key", Context.New("user"), defaultConfig);

        Assert.True(result.Enabled);
        Assert.Equal("fallback", result.Instructions);

        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s => s.Contains("AI Config mode mismatch") && s.Contains("Returning caller's default")),
            It.Is<object[]>(args => args.Length == 3 && (string)args[0] == "agent-key")
        ), Times.Once);
    }

    [Fact]
    public void AgentConfig_DisabledVariation_ReturnsDisabledAgentWithoutModeMismatchWarning()
    {
        var (mockClient, mockLogger, client) = MakeClient();

        // A disabled variation is served with no mode field, so its mode defaults to "completion".
        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": false}
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("agent-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var defaultConfig = LdAiAgentConfigDefault.New().SetInstructions("fallback").Build();
        var result = client.AgentConfig("agent-key", Context.New("user"), defaultConfig);

        Assert.IsType<LdAiAgentConfig>(result);
        Assert.False(result.Enabled);

        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s => s.Contains("AI Config mode mismatch")),
            It.IsAny<object[]>()
        ), Times.Never);
    }

    [Fact]
    public void AgentConfig_InstructionsInterpolated()
    {
        var (mockClient, _, client) = MakeClient();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {},
                              "instructions": "Hello {{name}}, you specialize in {{topic}}"
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("agent-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var variables = new Dictionary<string, object> { ["name"] = "Alice", ["topic"] = "physics" };
        var result = client.AgentConfig("agent-key", Context.New("user"), null, variables);

        Assert.Equal("Hello Alice, you specialize in physics", result.Instructions);
    }

    [Fact]
    public void AgentConfig_LdCtxInterpolatedInInstructions()
    {
        var (mockClient, _, client) = MakeClient();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {},
                              "instructions": "Serving context key: {{ldctx.key}}"
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("agent-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.AgentConfig("agent-key", Context.New(ContextKind.Default, "ctx-key-123"));

        Assert.Equal("Serving context key: ctx-key-123", result.Instructions);
    }

    [Fact]
    public void AgentConfig_FiresUsageEvent()
    {
        var (mockClient, _, client) = MakeClient();
        var context = Context.New(ContextKind.Default, "user");

        mockClient.Setup(x => x.JsonVariation("my-agent", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["mode"] = LdValue.Of("agent")
                }),
                ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>()),
                ["provider"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>())
            }));

        client.AgentConfig("my-agent", context);

        mockClient.Verify(x => x.Track(
            "$ld:ai:usage:agent-config",
            context,
            LdValue.Of("my-agent"),
            1), Times.Once);
    }

    // ── AgentConfigs ─────────────────────────────────────────────────────────

    [Fact]
    public void AgentConfigs_BatchRetrieval_ReturnsBothConfigs()
    {
        var (mockClient, _, client) = MakeClient();
        var context = Context.New(ContextKind.Default, "user");

        SetupAgentJson(mockClient, "agent-1", "First agent");
        SetupAgentJson(mockClient, "agent-2", "Second agent");

        var requests = new[]
        {
            new AgentConfigRequest { Key = "agent-1" },
            new AgentConfigRequest { Key = "agent-2" }
        };

        var result = client.AgentConfigs(requests, context);

        Assert.Equal(2, result.Count);
        Assert.Equal("First agent", result["agent-1"].Instructions);
        Assert.Equal("Second agent", result["agent-2"].Instructions);
    }

    [Fact]
    public void AgentConfigs_FiresOnlyAggregateEvent()
    {
        var (mockClient, _, client) = MakeClient();
        var context = Context.New(ContextKind.Default, "user");

        SetupAgentJson(mockClient, "agent-1", "A");
        SetupAgentJson(mockClient, "agent-2", "B");

        var requests = new[]
        {
            new AgentConfigRequest { Key = "agent-1" },
            new AgentConfigRequest { Key = "agent-2" }
        };

        client.AgentConfigs(requests, context);

        // The batch method must NOT fire individual $ld:ai:usage:agent-config events
        mockClient.Verify(x => x.Track("$ld:ai:usage:agent-config", context, It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);

        // One aggregate $ld:ai:usage:agent-configs with count = 2
        mockClient.Verify(x => x.Track("$ld:ai:usage:agent-configs", context, LdValue.Of(2), 2), Times.Once);
    }

    [Fact]
    public void AgentConfigs_EmptyBatch_ReturnsEmptyAndFiresEvent()
    {
        var (mockClient, _, client) = MakeClient();
        var context = Context.New(ContextKind.Default, "user");

        var result = client.AgentConfigs(new AgentConfigRequest[0], context);

        Assert.Empty(result);
        mockClient.Verify(x => x.Track("$ld:ai:usage:agent-configs", context, LdValue.Of(0), 0), Times.Once);
    }

    // ── JudgeConfig ──────────────────────────────────────────────────────────

    [Fact]
    public void JudgeConfig_BasicRetrieval_ReturnsCorrectFields()
    {
        var (mockClient, _, client) = MakeClient();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "judge"},
                              "model": {"name": "judge-model"},
                              "provider": {"name": "anthropic"},
                              "messages": [
                                {"content": "Rate the response", "role": "system"}
                              ],
                              "evaluationMetricKey": "$ld:ai:judge:relevance"
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("judge-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.JudgeConfig("judge-key", Context.New("user"));

        Assert.Equal("judge-key", result.Key);
        Assert.True(result.Enabled);
        Assert.Equal("$ld:ai:judge:relevance", result.EvaluationMetricKey);
        Assert.Equal("judge-model", result.Model.Name);
        Assert.Collection(result.Messages,
            m =>
            {
                Assert.Equal("Rate the response", m.Content);
                Assert.Equal(LdAiConfigTypes.Role.System, m.Role);
            });
        Assert.NotNull(result.CreateTracker());
    }

    [Fact]
    public void JudgeConfig_ModeMismatch_ReturnsCallerDefault()
    {
        var (mockClient, _, client) = MakeClient();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {}
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("judge-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var defaultConfig = LdAiJudgeConfigDefault.New()
            .SetEvaluationMetricKey("$ld:ai:judge:fallback")
            .Build();

        var result = client.JudgeConfig("judge-key", Context.New("user"), defaultConfig);

        Assert.Equal("$ld:ai:judge:fallback", result.EvaluationMetricKey);
    }

    [Fact]
    public void JudgeConfig_DisabledVariation_ReturnsDisabledJudgeWithoutModeMismatchWarning()
    {
        var (mockClient, mockLogger, client) = MakeClient();

        // A disabled variation is served with no mode field, so its mode defaults to "completion".
        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": false}
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("judge-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var defaultConfig = LdAiJudgeConfigDefault.New()
            .SetEvaluationMetricKey("$ld:ai:judge:fallback")
            .Build();

        var result = client.JudgeConfig("judge-key", Context.New("user"), defaultConfig);

        Assert.IsType<LdAiJudgeConfig>(result);
        Assert.False(result.Enabled);

        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s => s.Contains("AI Config mode mismatch")),
            It.IsAny<object[]>()
        ), Times.Never);
    }

    [Fact]
    public void JudgeConfig_FiresUsageEvent()
    {
        var (mockClient, _, client) = MakeClient();
        var context = Context.New(ContextKind.Default, "user");

        mockClient.Setup(x => x.JsonVariation("my-judge", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["mode"] = LdValue.Of("judge")
                }),
                ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>()),
                ["messages"] = LdValue.ArrayOf()
            }));

        client.JudgeConfig("my-judge", context);

        mockClient.Verify(x => x.Track(
            "$ld:ai:usage:judge-config",
            context,
            LdValue.Of("my-judge"),
            1), Times.Once);
    }

    [Fact]
    public void JudgeConfig_MessagesInterpolated()
    {
        var (mockClient, _, client) = MakeClient();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "judge"},
                              "model": {},
                              "messages": [
                                {"content": "Evaluate for metric: {{metric}}", "role": "user"}
                              ]
                            }
                            """;

        mockClient.Setup(x => x.JsonVariation("judge-key", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var variables = new Dictionary<string, object> { ["metric"] = "relevance" };
        var result = client.JudgeConfig("judge-key", Context.New("user"), null, variables);

        Assert.Collection(result.Messages,
            m => Assert.Equal("Evaluate for metric: relevance", m.Content));
    }

    // ── AgentConfigTemplate ──────────────────────────────────────────────────

    [Fact]
    public void AgentConfigTemplate_PreservesPlaceholders()
    {
        var (mockClient, _, client) = MakeClient();
        const string json = """
            {
              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
              "model": {},
              "instructions": "Hello {{name}}, specialize in {{topic}}"
            }
            """;
        mockClient.Setup(x => x.JsonVariation("agent-flag", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.AgentConfigTemplate("agent-flag", Context.New("user"));

        Assert.Equal("Hello {{name}}, specialize in {{topic}}", result.Instructions);
        Assert.True(result.Enabled);
    }

    [Fact]
    public void AgentConfigTemplate_PreservesLdCtxPlaceholder()
    {
        var (mockClient, _, client) = MakeClient();
        const string json = """
            {
              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
              "model": {},
              "instructions": "Serving context key: {{ldctx.key}}"
            }
            """;
        mockClient.Setup(x => x.JsonVariation("agent-flag", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.AgentConfigTemplate("agent-flag", Context.New(ContextKind.Default, "ctx-key-123"));

        Assert.Equal("Serving context key: {{ldctx.key}}", result.Instructions);
    }

    [Fact]
    public void AgentConfigTemplate_FiresTemplateTrackingEvent()
    {
        var (mockClient, _, client) = MakeClient();
        var context = Context.New(ContextKind.Default, "user");
        mockClient.Setup(x => x.JsonVariation("my-agent", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["mode"] = LdValue.Of("agent")
                }),
                ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>())
            }));

        client.AgentConfigTemplate("my-agent", context);

        mockClient.Verify(x => x.Track(
            "$ld:ai:usage:agent-config-template",
            context,
            LdValue.Of("my-agent"),
            1), Times.Once);
        mockClient.Verify(x => x.Track(
            "$ld:ai:usage:agent-config",
            It.IsAny<Context>(), It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void AgentConfigTemplate_UsesDisabledDefaultWhenNoDefaultProvided()
    {
        var (mockClient, _, client) = MakeClient();
        mockClient.Setup(x => x.JsonVariation("my-agent", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Null);

        var result = client.AgentConfigTemplate("my-agent", Context.New("user"));

        Assert.False(result.Enabled);
    }

    [Fact]
    public void AgentConfigTemplate_CreateTrackerIsNonNull()
    {
        var (mockClient, _, client) = MakeClient();
        const string json = """
            {
              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
              "model": {}
            }
            """;
        mockClient.Setup(x => x.JsonVariation("agent-flag", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.AgentConfigTemplate("agent-flag", Context.New("user"));

        Assert.NotNull(result.CreateTracker());
    }

    // ── JudgeConfigTemplate ──────────────────────────────────────────────────

    [Fact]
    public void JudgeConfigTemplate_PreservesPlaceholders()
    {
        var (mockClient, _, client) = MakeClient();
        const string json = """
            {
              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "judge"},
              "model": {},
              "messages": [
                {"content": "Evaluate for metric: {{metric}}", "role": "user"},
                {"content": "Score: {{score}}", "role": "system"}
              ]
            }
            """;
        mockClient.Setup(x => x.JsonVariation("judge-flag", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.JudgeConfigTemplate("judge-flag", Context.New("user"));

        Assert.Collection(result.Messages,
            m => Assert.Equal("Evaluate for metric: {{metric}}", m.Content),
            m => Assert.Equal("Score: {{score}}", m.Content));
        Assert.True(result.Enabled);
    }

    [Fact]
    public void JudgeConfigTemplate_PreservesLdCtxPlaceholder()
    {
        var (mockClient, _, client) = MakeClient();
        const string json = """
            {
              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "judge"},
              "model": {},
              "messages": [
                {"content": "Context key: {{ldctx.key}}", "role": "system"}
              ]
            }
            """;
        mockClient.Setup(x => x.JsonVariation("judge-flag", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.JudgeConfigTemplate("judge-flag", Context.New(ContextKind.Default, "ctx-key-123"));

        Assert.Collection(result.Messages,
            m => Assert.Equal("Context key: {{ldctx.key}}", m.Content));
    }

    [Fact]
    public void JudgeConfigTemplate_FiresTemplateTrackingEvent()
    {
        var (mockClient, _, client) = MakeClient();
        var context = Context.New(ContextKind.Default, "user");
        mockClient.Setup(x => x.JsonVariation("my-judge", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["mode"] = LdValue.Of("judge")
                }),
                ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>()),
                ["messages"] = LdValue.ArrayOf()
            }));

        client.JudgeConfigTemplate("my-judge", context);

        mockClient.Verify(x => x.Track(
            "$ld:ai:usage:judge-config-template",
            context,
            LdValue.Of("my-judge"),
            1), Times.Once);
        mockClient.Verify(x => x.Track(
            "$ld:ai:usage:judge-config",
            It.IsAny<Context>(), It.IsAny<LdValue>(), It.IsAny<double>()), Times.Never);
    }

    [Fact]
    public void JudgeConfigTemplate_UsesDisabledDefaultWhenNoDefaultProvided()
    {
        var (mockClient, _, client) = MakeClient();
        mockClient.Setup(x => x.JsonVariation("my-judge", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Null);

        var result = client.JudgeConfigTemplate("my-judge", Context.New("user"));

        Assert.False(result.Enabled);
    }

    [Fact]
    public void JudgeConfigTemplate_CreateTrackerIsNonNull()
    {
        var (mockClient, _, client) = MakeClient();
        const string json = """
            {
              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "judge"},
              "model": {},
              "messages": []
            }
            """;
        mockClient.Setup(x => x.JsonVariation("judge-flag", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(json));

        var result = client.JudgeConfigTemplate("judge-flag", Context.New("user"));

        Assert.NotNull(result.CreateTracker());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void SetupAgentJson(Mock<ILaunchDarklyClient> mockClient, string key, string instructions)
    {
        mockClient.Setup(x => x.JsonVariation(key, It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["mode"] = LdValue.Of("agent")
                }),
                ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>()),
                ["provider"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>()),
                ["instructions"] = LdValue.Of(instructions)
            }));
    }
}
