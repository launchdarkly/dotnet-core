using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class InterpolationTests
{
    private string Eval(string prompt, Context context, IReadOnlyDictionary<string, object> variables)
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();


        // The replacement is done this way because to use string.Format, we'd need to escape the curly braces.
        var configJson = """
                        {
                            "_ldMeta": {"versionKey": "1", "enabled": true},
                            "model": {},
                            "prompt": [
                                {
                                    "content": "<do-not-use-in-any-tests-prompt-placeholder>",
                                    "role": "System"
                                }
                            ]
                        }
                        """.Replace("<do-not-use-in-any-tests-prompt-placeholder>", prompt);


        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(configJson));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var tracker = client.ModelConfig("foo", context, LdAiConfig.Disabled, variables);

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

    [Fact]
    public void TestInterpolationWithArraySectionWorks()
    {
        var context = Context.New("user-key");
        var variables = new Dictionary<string, object>
        {
            { "adjectives", new List<string> { "hello", "world", "!" } }
        };

        var result = Eval("{{#adjectives}}{{.}} {{/adjectives}}", context, variables);
        Assert.Equal("hello world ! ", result);
    }

    [Fact]
    public void TestInterpolationMalformed()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string configJson = """
                                  {
                                      "_ldMeta": {"versionKey": "1", "enabled": true},
                                      "model": {},
                                      "prompt": [
                                          {
                                              "content": "This is a {{ malformed }]} prompt",
                                              "role": "System"
                                          }
                                      ]
                                  }
                                  """;


        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(configJson));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        mockLogger.Setup(x => x.Error(It.IsAny<string>()));

        var client = new LdAiClient(mockClient.Object);
        var tracker = client.ModelConfig("foo", Context.New("key"), LdAiConfig.Disabled);
        Assert.False(tracker.Config.Enabled);
    }

    [Fact]
    public void TestInterpolationWithBasicContext()
    {
        var context = Context.Builder(ContextKind.Default, "123")
            .Set("name", "Sandy").Build();
        var result1 = Eval("I'm a {{ ldctx.kind}} with key {{ ldctx.key }}, named {{ ldctx.name }}", context, null);
        Assert.Equal("I'm a user with key 123, named Sandy", result1);
    }

    [Fact]
    public void TestInterpolationWithNestedContextAttributes()
    {
        var context = Context.Builder(ContextKind.Default, "123")
            .Set("stats", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "power", LdValue.Of(9000) }
            })).Build();
        var result = Eval("I can ingest over {{ ldctx.stats.power }} tokens per second!", context, null);
        Assert.Equal("I can ingest over 9000 tokens per second!", result);
    }
}
