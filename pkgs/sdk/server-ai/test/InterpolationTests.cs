using System.Collections.Generic;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class InterpolationTests
{
    private string Eval(string prompt, Context context, IReadOnlyDictionary<string, object> variables)
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        var config = new AiConfig
        {
            Meta = new Meta
            {
                Enabled = true,
                VersionKey = "1"
            },
            Model = null,
            Prompt = new List<Message>
            {
                new()
                {
                    Content = prompt,
                    Role = Role.System
                }
            }
        };

        var json = JsonSerializer.Serialize(config);

        mockClient.Setup(x =>
            x.JsonVariationDetail("foo", It.IsAny<Context>(), LdValue.Null)).Returns(
            new EvaluationDetail<LdValue>(LdValue.Parse(json), 0, EvaluationReason.FallthroughReason));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var tracker = client.GetModelConfig("foo", context, LdAiConfig.Disabled, variables);

        return tracker.Config.Prompt[0].Content;
    }

    [Theory]
    [InlineData("{{ adjective}}")]
    [InlineData("{{ adjective.nested.deep }}")]
    [InlineData("{{ ldctx.this_is_not_a_variable }}")]
    public void TestInterpolationMissingVariables(string variable)
    {

        var context = Context.New("user-key");
        var result = Eval($"I am an ({variable}) LLM", context, null);
        Assert.Equal("I am an () LLM", result);
    }

    [Theory]
    [InlineData("awesome")]
    [InlineData("slow")]
    [InlineData("all powerful")]
    public void TestInterpolationWithVariables(string description)
    {
        var context = Context.New("user-key");
        var variables = new Dictionary<string, object>
        {
            { "adjective", description }
        };
        var result = Eval("I am an {{ adjective }} LLM", context, variables);
        Assert.Equal($"I am an {description} LLM", result);
    }

    [Fact]
    public void TestInterpolationWithMultipleVariables()
    {
        var context = Context.New("user-key");
        var variables = new Dictionary<string, object>
        {
            { "adjective", "awesome" },
            { "noun", "robot" },
            { "stats", new Dictionary<string, object>
                {
                    { "power", 9000 }
                }
            }
        };

        var result = Eval("I am an {{ adjective }} {{ noun }} with power over {{ stats.power }}", context, variables);
        Assert.Equal("I am an awesome robot with power over 9000", result);
    }

    [Theory]
    [InlineData("{{ adjectives.0 }}")]
    [InlineData("{{ adjectives[0] }}")]
    public void TestInterpolationWithArrayAccessDoesNotWork(string accessor)
    {
        var context = Context.New("user-key");
        var variables = new Dictionary<string, object>
        {
            { "adjectives", new List<string> { "awesome", "slow", "all powerful" } }
        };

        var result = Eval($"I am an ({accessor}) LLM", context, variables);
        Assert.Equal("I am an () LLM", result);
    }

}
