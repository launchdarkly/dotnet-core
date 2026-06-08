using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class LdAiAgentConfigTest
{
    private static (Mock<ILaunchDarklyClient> MockClient, Mock<ILogger> MockLogger, ConfigFactory Factory) MakeFactory()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
        var factory = new ConfigFactory(mockClient.Object, mockLogger.Object);
        return (mockClient, mockLogger, factory);
    }

    [Fact]
    public void AgentConfig_PropertiesAreAccessible()
    {
        var (mockClient, mockLogger, factory) = MakeFactory();

        var tools = new Dictionary<string, LdAiConfigTypes.Tool>
        {
            ["search"] = new LdAiConfigTypes.Tool("search", "Web search", "function",
                new Dictionary<string, LdValue>(), new Dictionary<string, LdValue>())
        };

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {"name": "gpt-4o"},
                              "provider": {"name": "openai"},
                              "instructions": "You are a helpful assistant",
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

        var result = factory.BuildAgentConfig(
            "my-key",
            LdValue.Parse(json),
            Context.New("user"),
            LdAiAgentConfigDefault.Disabled,
            null);

        Assert.Equal("my-key", result.Key);
        Assert.True(result.Enabled);
        Assert.Equal("v1", result.VariationKey);
        Assert.Equal("You are a helpful assistant", result.Instructions);
        Assert.Equal("gpt-4o", result.Model.Name);
        Assert.Equal("openai", result.Provider.Name);
        Assert.Single(result.Tools);
        Assert.True(result.Tools.ContainsKey("search"));
        Assert.NotNull(result.CreateTracker());
    }

    [Fact]
    public void BuildAgentConfig_HappyPath_ParsesAllFields()
    {
        var (_, _, factory) = MakeFactory();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v2", "enabled": true, "mode": "agent"},
                              "model": {"name": "claude-3", "parameters": {"temperature": 0.7}},
                              "provider": {"name": "anthropic"},
                              "instructions": "Be concise",
                              "tools": {
                                "calculator": {
                                  "name": "calculator",
                                  "description": "Performs math",
                                  "type": "function",
                                  "parameters": {"expression": "string"},
                                  "customParameters": {"precision": 5}
                                }
                              }
                            }
                            """;

        var result = factory.BuildAgentConfig(
            "agent-key",
            LdValue.Parse(json),
            Context.New("user"),
            LdAiAgentConfigDefault.Disabled,
            null);

        Assert.True(result.Enabled);
        Assert.Equal("Be concise", result.Instructions);
        Assert.Equal("claude-3", result.Model.Name);
        Assert.Equal(0.7, result.Model.Parameters["temperature"].AsDouble);
        Assert.Equal("anthropic", result.Provider.Name);
        Assert.Equal(1, result.Tools.Count);
        Assert.Equal("calculator", result.Tools["calculator"].Name);
        Assert.Equal("Performs math", result.Tools["calculator"].Description);
        Assert.Equal(5, result.Tools["calculator"].CustomParameters["precision"].AsInt);
    }

    [Fact]
    public void BuildAgentConfig_ModeMismatch_ReturnsCallerDefault()
    {
        var (_, mockLogger, factory) = MakeFactory();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "completion"},
                              "model": {"name": "should-be-ignored"},
                              "instructions": "should be ignored"
                            }
                            """;

        var defaultConfig = LdAiAgentConfigDefault.New()
            .SetInstructions("default instructions")
            .SetModelName("default-model")
            .Build();

        var result = factory.BuildAgentConfig(
            "agent-key",
            LdValue.Parse(json),
            Context.New("user"),
            defaultConfig,
            null);

        Assert.True(result.Enabled);
        Assert.Equal("default instructions", result.Instructions);
        Assert.Equal("default-model", result.Model.Name);

        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s =>
                s.Contains("AI Config mode mismatch") &&
                s.Contains("Returning caller's default")),
            It.Is<object[]>(args =>
                args.Length == 3 &&
                (string)args[0] == "agent-key" &&
                (string)args[1] == "agent" &&
                (string)args[2] == "completion")
        ), Times.Once);
    }

    [Fact]
    public void BuildAgentConfig_NonObjectVariation_ReturnsCallerDefault()
    {
        var (_, mockLogger, factory) = MakeFactory();

        var defaultConfig = LdAiAgentConfigDefault.New()
            .SetInstructions("fallback")
            .Build();

        var result = factory.BuildAgentConfig(
            "agent-key",
            LdValue.Of(42),
            Context.New("user"),
            defaultConfig,
            null);

        Assert.True(result.Enabled);
        Assert.Equal("fallback", result.Instructions);

        mockLogger.Verify(x => x.Error(
            It.Is<string>(s =>
                s.Contains("AI Config") &&
                s.Contains("is not an object") &&
                s.Contains("using caller's default")),
            It.Is<object[]>(args =>
                args.Length == 2 &&
                (string)args[0] == "agent-key" &&
                (LdValueType)args[1] == LdValueType.Number)
        ), Times.Once);
    }

    [Fact]
    public void BuildAgentConfig_ParsesJudgeConfiguration()
    {
        var (_, _, factory) = MakeFactory();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {},
                              "provider": {},
                              "instructions": "You are helpful",
                              "judgeConfiguration": {
                                "judges": [
                                  {"key": "accuracy-judge", "samplingRate": 0.5},
                                  {"key": "relevance-judge", "samplingRate": 1.0}
                                ]
                              }
                            }
                            """;

        var result = factory.BuildAgentConfig(
            "agent-key",
            LdValue.Parse(json),
            Context.New("user"),
            LdAiAgentConfigDefault.Disabled,
            null);

        Assert.NotNull(result.JudgeConfiguration);
        Assert.Equal(2, result.JudgeConfiguration.Judges.Count);
        Assert.Equal("accuracy-judge", result.JudgeConfiguration.Judges[0].Key);
        Assert.Equal(0.5, result.JudgeConfiguration.Judges[0].SamplingRate);
        Assert.Equal("relevance-judge", result.JudgeConfiguration.Judges[1].Key);
        Assert.Equal(1.0, result.JudgeConfiguration.Judges[1].SamplingRate);
    }

    [Fact]
    public void BuildAgentConfig_NoJudgeConfiguration_IsNull()
    {
        var (_, _, factory) = MakeFactory();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {},
                              "provider": {},
                              "instructions": "You are helpful"
                            }
                            """;

        var result = factory.BuildAgentConfig(
            "agent-key",
            LdValue.Parse(json),
            Context.New("user"),
            LdAiAgentConfigDefault.Disabled,
            null);

        Assert.Null(result.JudgeConfiguration);
    }

    [Fact]
    public void BuildAgentConfig_DefaultJudgeConfiguration_PreservedThroughToLdValueRoundTrip()
    {
        var (_, _, factory) = MakeFactory();

        var judges = new List<LdAiConfigTypes.JudgeConfiguration.Judge>
        {
            new("accuracy-judge", 0.5),
            new("relevance-judge", 1.0)
        };
        var judgeConfig = new LdAiConfigTypes.JudgeConfiguration(judges);

        var defaultConfig = LdAiAgentConfigDefault.New()
            .SetInstructions("fallback instructions")
            .SetModelName("fallback-model")
            .SetJudgeConfiguration(judgeConfig)
            .Build();

        // Simulate what LdAiClient does: serialize the default to LdValue, then feed it back
        // through BuildAgentConfig (as would happen when JsonVariation returns the default).
        var result = factory.BuildAgentConfig(
            "agent-key",
            defaultConfig.ToLdValue(),
            Context.New("user"),
            LdAiAgentConfigDefault.Disabled,
            null);

        Assert.NotNull(result.JudgeConfiguration);
        Assert.Equal(2, result.JudgeConfiguration.Judges.Count);
        Assert.Equal("accuracy-judge", result.JudgeConfiguration.Judges[0].Key);
        Assert.Equal(0.5, result.JudgeConfiguration.Judges[0].SamplingRate);
        Assert.Equal("relevance-judge", result.JudgeConfiguration.Judges[1].Key);
        Assert.Equal(1.0, result.JudgeConfiguration.Judges[1].SamplingRate);
    }

    [Fact]
    public void BuildAgentConfig_InstructionsInterpolated()
    {
        var (_, _, factory) = MakeFactory();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {},
                              "provider": {},
                              "instructions": "Hello {{name}}, you specialize in {{topic}}"
                            }
                            """;

        var variables = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["topic"] = "astronomy"
        };

        var result = factory.BuildAgentConfig(
            "agent-key",
            LdValue.Parse(json),
            Context.New("user"),
            LdAiAgentConfigDefault.Disabled,
            variables);

        Assert.Equal("Hello Alice, you specialize in astronomy", result.Instructions);
    }
}
