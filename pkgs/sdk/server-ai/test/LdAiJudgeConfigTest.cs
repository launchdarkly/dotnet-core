using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class LdAiJudgeConfigTest
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
    public void JudgeConfig_PropertiesAreAccessible()
    {
        var (_, _, factory) = MakeFactory();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "judge"},
                              "model": {"name": "gpt-4o"},
                              "provider": {"name": "openai"},
                              "messages": [
                                {"content": "Rate the response", "role": "system"}
                              ],
                              "evaluationMetricKey": "$ld:ai:judge:relevance"
                            }
                            """;

        var result = factory.BuildJudgeConfig(
            "judge-key",
            LdValue.Parse(json),
            Context.New("user"),
            LdAiJudgeConfigDefault.Disabled,
            null);

        Assert.Equal("judge-key", result.Key);
        Assert.True(result.Enabled);
        Assert.Equal("v1", result.VariationKey);
        Assert.Equal("$ld:ai:judge:relevance", result.EvaluationMetricKey);
        Assert.Equal("gpt-4o", result.Model.Name);
        Assert.Equal("openai", result.Provider.Name);
        Assert.Collection(result.Messages,
            m =>
            {
                Assert.Equal("Rate the response", m.Content);
                Assert.Equal(LdAiConfigTypes.Role.System, m.Role);
            });
        Assert.NotNull(result.CreateTracker());
    }

    [Fact]
    public void BuildJudgeConfig_HappyPath_ParsesAllFields()
    {
        var (_, _, factory) = MakeFactory();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v2", "enabled": true, "mode": "judge"},
                              "model": {"name": "judge-model"},
                              "provider": {"name": "anthropic"},
                              "messages": [
                                {"content": "Is the response relevant?", "role": "user"},
                                {"content": "Context: {{topic}}", "role": "system"}
                              ],
                              "evaluationMetricKey": "$ld:ai:judge:coherence"
                            }
                            """;

        var variables = new Dictionary<string, object> { ["topic"] = "science" };

        var result = factory.BuildJudgeConfig(
            "judge-key",
            LdValue.Parse(json),
            Context.New("user"),
            LdAiJudgeConfigDefault.Disabled,
            variables);

        Assert.Equal("$ld:ai:judge:coherence", result.EvaluationMetricKey);
        Assert.Equal("judge-model", result.Model.Name);
        Assert.Equal("anthropic", result.Provider.Name);
        Assert.Equal(2, result.Messages.Count);
        Assert.Equal("Is the response relevant?", result.Messages[0].Content);
        Assert.Equal("Context: science", result.Messages[1].Content);
    }

    [Fact]
    public void BuildJudgeConfig_ModeMismatch_ReturnsCallerDefault()
    {
        var (_, mockLogger, factory) = MakeFactory();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {"name": "should-be-ignored"}
                            }
                            """;

        var defaultConfig = LdAiJudgeConfigDefault.New()
            .AddMessage("default message", LdAiConfigTypes.Role.System)
            .SetEvaluationMetricKey("$ld:ai:judge:default")
            .Build();

        var result = factory.BuildJudgeConfig(
            "judge-key",
            LdValue.Parse(json),
            Context.New("user"),
            defaultConfig,
            null);

        Assert.True(result.Enabled);
        Assert.Equal("$ld:ai:judge:default", result.EvaluationMetricKey);
        Assert.Collection(result.Messages,
            m => Assert.Equal("default message", m.Content));

        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s =>
                s.Contains("AI Config mode mismatch") &&
                s.Contains("Returning caller's default")),
            It.Is<object[]>(args =>
                args.Length == 3 &&
                (string)args[0] == "judge-key" &&
                (string)args[1] == "judge" &&
                (string)args[2] == "agent")
        ), Times.Once);
    }

    [Fact]
    public void BuildJudgeConfig_NonObjectVariation_ReturnsCallerDefault()
    {
        var (_, mockLogger, factory) = MakeFactory();

        var defaultConfig = LdAiJudgeConfigDefault.New()
            .SetEvaluationMetricKey("$ld:ai:judge:fallback")
            .Build();

        var result = factory.BuildJudgeConfig(
            "judge-key",
            LdValue.Of(42),
            Context.New("user"),
            defaultConfig,
            null);

        Assert.Equal("$ld:ai:judge:fallback", result.EvaluationMetricKey);

        mockLogger.Verify(x => x.Error(
            It.Is<string>(s =>
                s.Contains("AI Config") &&
                s.Contains("is not an object") &&
                s.Contains("using caller's default")),
            It.Is<object[]>(args =>
                args.Length == 2 &&
                (string)args[0] == "judge-key" &&
                (LdValueType)args[1] == LdValueType.Number)
        ), Times.Once);
    }

    [Fact]
    public void JudgeResult_BackwardCompat_OldConstructorStillWorks()
    {
        // Five-argument form (no ErrorMessage/Reasoning) must still compile and work.
        var result = new JudgeResult("my-metric", 0.75, sampled: true, success: true, judgeConfigKey: "j1");

        Assert.Equal("my-metric", result.MetricKey);
        Assert.Equal(0.75, result.Score);
        Assert.True(result.Sampled);
        Assert.True(result.Success);
        Assert.Equal("j1", result.JudgeConfigKey);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Reasoning);
    }

    [Fact]
    public void JudgeResult_ErrorMessageRoundTrip()
    {
        var result = new JudgeResult("metric", 0, success: false, errorMessage: "provider down");

        Assert.False(result.Success);
        Assert.Equal("provider down", result.ErrorMessage);
        Assert.Null(result.Reasoning);
    }

    [Fact]
    public void JudgeResult_ReasoningRoundTrip()
    {
        var result = new JudgeResult("metric", 0.9, sampled: true, success: true, reasoning: "Very coherent");

        Assert.True(result.Success);
        Assert.Equal("Very coherent", result.Reasoning);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void JudgeResult_AllFieldsSet()
    {
        var result = new JudgeResult(
            "metric", 0.5,
            sampled: true,
            success: false,
            judgeConfigKey: "jk",
            errorMessage: "bad input",
            reasoning: "n/a");

        Assert.Equal("metric", result.MetricKey);
        Assert.Equal(0.5, result.Score);
        Assert.True(result.Sampled);
        Assert.False(result.Success);
        Assert.Equal("jk", result.JudgeConfigKey);
        Assert.Equal("bad input", result.ErrorMessage);
        Assert.Equal("n/a", result.Reasoning);
    }

    [Fact]
    public void DefaultTypes_BehaviorAndToLdValue()
    {
        // LdAiAgentConfigDefault
        var agentDisabled = LdAiAgentConfigDefault.Disabled;
        Assert.False(agentDisabled.Enabled);

        var agentEnabled = LdAiAgentConfigDefault.New()
            .SetInstructions("Do stuff")
            .SetModelName("gpt-4o")
            .SetModelProviderName("openai")
            .Build();
        Assert.True(agentEnabled.Enabled);
        Assert.Equal("Do stuff", agentEnabled.Instructions);

        var agentLdValue = agentEnabled.ToLdValue();
        Assert.Equal("agent", agentLdValue.Get("_ldMeta").Get("mode").AsString);
        Assert.Equal("Do stuff", agentLdValue.Get("instructions").AsString);
        Assert.Equal("gpt-4o", agentLdValue.Get("model").Get("name").AsString);

        // LdAiJudgeConfigDefault
        var judgeDisabled = LdAiJudgeConfigDefault.Disabled;
        Assert.False(judgeDisabled.Enabled);

        var judgeEnabled = LdAiJudgeConfigDefault.New()
            .AddMessage("Rate this", LdAiConfigTypes.Role.System)
            .SetEvaluationMetricKey("$ld:ai:judge:relevance")
            .SetModelName("judge-model")
            .Build();
        Assert.True(judgeEnabled.Enabled);
        Assert.Equal("$ld:ai:judge:relevance", judgeEnabled.EvaluationMetricKey);

        var judgeLdValue = judgeEnabled.ToLdValue();
        Assert.Equal("judge", judgeLdValue.Get("_ldMeta").Get("mode").AsString);
        Assert.Equal("$ld:ai:judge:relevance", judgeLdValue.Get("evaluationMetricKey").AsString);
        Assert.Equal(1, judgeLdValue.Get("messages").Count);
        Assert.Equal("judge-model", judgeLdValue.Get("model").Get("name").AsString);
    }
}
