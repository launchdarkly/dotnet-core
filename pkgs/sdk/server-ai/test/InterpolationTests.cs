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
                            "messages": [
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

        return tracker.Config.Messages[0].Content;
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
                                      "messages": [
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

    [Fact]
    public void TestInterpolationWithMultiKindContext()
    {
        var user = Context.Builder(ContextKind.Default, "123")
            .Set("cat_ownership", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "count", LdValue.Of(12) }
            })).Build();

        var cat = Context.Builder(ContextKind.Of("cat"), "456")
            .Set("health", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "hunger", LdValue.Of("off the charts") }
            })).Build();

        var context = Context.MultiBuilder().Add(user).Add(cat).Build();

        var nestedVars = Eval("As an owner of {{ ldctx.user.cat_ownership.count }} cats, I must report that my cat's hunger level is {{ ldctx.cat.health.hunger }}!", context, null);
        Assert.Equal("As an owner of 12 cats, I must report that my cat's hunger level is off the charts!", nestedVars);

        var canonicalKeys = Eval("multi={{ ldctx.key }} user={{ ldctx.user.key }} cat={{ ldctx.cat.key }}", context, null);
        Assert.Equal("multi=cat:456:user:123 user=123 cat=456", canonicalKeys);
    }

    [Fact]
    public void TestInterpolationMultiKindDoesNotHaveAnonymousAttribute()
    {
        var user = Context.Builder(ContextKind.Default, "123")
            .Set("cat_ownership", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "count", LdValue.Of(12) }
            })).Build();

        var cat = Context.Builder(ContextKind.Of("cat"), "456")
            .Set("health", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "hunger", LdValue.Of("off the charts") }
            })).Build();

        var context = Context.MultiBuilder().Add(user).Add(cat).Build();

        var result = Eval("anonymous=<{{ ldctx.anonymous }}>", context, null);
        Assert.Equal("anonymous=<>", result);
    }

    [Fact]
    public void TestInterpolationMultiKindContextHasKindMulti()
    {
        var user = Context.Builder(ContextKind.Default, "123")
            .Set("cat_ownership", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "count", LdValue.Of(12) }
            })).Build();

        var cat = Context.Builder(ContextKind.Of("cat"), "456")
            .Set("health", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "hunger", LdValue.Of("off the charts") }
            })).Build();

        var context = Context.MultiBuilder().Add(user).Add(cat).Build();

        var result = Eval("kind={{ ldctx.kind }}", context, null);
        Assert.Equal("kind=multi", result);
    }
}
