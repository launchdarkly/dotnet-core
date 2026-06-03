using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class ConfigFactoryParserTest
{
    [Fact]
    public void ParseTools_ReturnsTwoEntries_WithCorrectCustomParameters()
    {
        var toolsValue = LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            ["search"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["name"] = LdValue.Of("search"),
                ["description"] = LdValue.Of("Searches the web"),
                ["type"] = LdValue.Of("function"),
                ["parameters"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["query"] = LdValue.Of("string")
                }),
                ["customParameters"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["timeout"] = LdValue.Of(30)
                })
            }),
            ["calculator"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["name"] = LdValue.Of("calculator"),
                ["description"] = LdValue.Of("Performs arithmetic"),
                ["type"] = LdValue.Of("function"),
                ["parameters"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>()),
                ["customParameters"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["precision"] = LdValue.Of(10)
                })
            })
        });

        // Build a root-level payload that also contains model.parameters.tools to confirm
        // they remain separate and are not mixed with the root tools map.
        var rootValue = LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            ["tools"] = toolsValue,
            ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["parameters"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["tools"] = LdValue.ArrayOf(LdValue.Of("some-tool"))
                })
            })
        });

        var tools = ConfigFactory.ParseTools(rootValue.Get("tools"));

        Assert.Equal(2, tools.Count);
        Assert.True(tools.ContainsKey("search"));
        Assert.True(tools.ContainsKey("calculator"));

        Assert.Equal(30, tools["search"].CustomParameters["timeout"].AsInt);
        Assert.Equal(10, tools["calculator"].CustomParameters["precision"].AsInt);
        Assert.Equal("string", tools["search"].Parameters["query"].AsString);

        // model.parameters.tools (opaque array) must be unaffected by root tools parsing.
        var modelTools = rootValue.Get("model").Get("parameters").Get("tools");
        Assert.Equal(LdValueType.Array, modelTools.Type);
        Assert.Equal(1, modelTools.Count);
    }

    [Fact]
    public void ParseJudgeConfiguration_ReturnsTwoEntries_WithCorrectKeysAndSamplingRates()
    {
        var rootValue = LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            ["judgeConfiguration"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["judges"] = LdValue.ArrayOf(
                    LdValue.ObjectFrom(new Dictionary<string, LdValue>
                    {
                        ["key"] = LdValue.Of("judge-relevance"),
                        ["samplingRate"] = LdValue.Of(0.5)
                    }),
                    LdValue.ObjectFrom(new Dictionary<string, LdValue>
                    {
                        ["key"] = LdValue.Of("judge-coherence"),
                        ["samplingRate"] = LdValue.Of(1.0)
                    })
                )
            })
        });

        var judgeConfig = ConfigFactory.ParseJudgeConfiguration(rootValue);

        Assert.NotNull(judgeConfig);
        Assert.Equal(2, judgeConfig.Judges.Count);
        Assert.Equal("judge-relevance", judgeConfig.Judges[0].Key);
        Assert.Equal(0.5, judgeConfig.Judges[0].SamplingRate);
        Assert.Equal("judge-coherence", judgeConfig.Judges[1].Key);
        Assert.Equal(1.0, judgeConfig.Judges[1].SamplingRate);
    }

    [Fact]
    public void ParseEvaluationMetricKey_ReturnsParsedString()
    {
        var rootValue = LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            ["evaluationMetricKey"] = LdValue.Of("$ld:ai:judge:relevance")
        });

        var key = ConfigFactory.ParseEvaluationMetricKey(rootValue);

        Assert.Equal("$ld:ai:judge:relevance", key);
    }

    [Fact]
    public void ParseInstructions_ReturnsRawUninterpolatedString()
    {
        var rootValue = LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            ["instructions"] = LdValue.Of("You are a helpful assistant specializing in {{topic}}")
        });

        var instructions = ConfigFactory.ParseInstructions(rootValue);

        Assert.Equal("You are a helpful assistant specializing in {{topic}}", instructions);
    }

    [Fact]
    public void ParseAllFields_WithNoNewFields_ReturnsNullOrEmptyWithoutError()
    {
        var rootValue = LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["enabled"] = LdValue.Of(true),
                ["variationKey"] = LdValue.Of("v1")
            }),
            ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["name"] = LdValue.Of("some-model")
            })
        });

        var instructions = ConfigFactory.ParseInstructions(rootValue);
        var tools = ConfigFactory.ParseTools(rootValue.Get("tools"));
        var judgeConfig = ConfigFactory.ParseJudgeConfiguration(rootValue);
        var evaluationMetricKey = ConfigFactory.ParseEvaluationMetricKey(rootValue);

        Assert.Null(instructions);
        Assert.Empty(tools);
        Assert.Null(judgeConfig);
        Assert.Null(evaluationMetricKey);
    }
}
