using System.Collections.Generic;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class LdAiClientTest
{
    [Fact]
    public void CanInstantiateWithServerSideClient()
    {
        var client = new LdClientAdapter(new LdClient(Configuration.Builder("key").Offline(true).Build()));
        var aiClient = new LdAiClient(client);
        var result = aiClient.CompletionConfig("foo", Context.New("key"), LdAiCompletionConfigDefault.Disabled);
        Assert.False(result.Enabled);
    }

    [Fact]
    public void ThrowsIfClientIsNull()
    {
        Assert.Throws<System.ArgumentNullException>(() => new LdAiClient(null));
    }

    [Fact]
    public void ReturnsDefaultConfigWhenGivenInvalidVariation()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        // Mimic JsonVariation's real contract: when evaluation fails, the supplied
        // defaultValue LdValue is returned. The AI SDK then tolerantly parses it as if
        // it were a real server response.
        mockClient.Setup(x =>
                x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns((string _, Context _, LdValue dv) => dv);


        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);


        var client = new LdAiClient(mockClient.Object);

        var defaultConfig = LdAiCompletionConfigDefault.New().AddMessage("Hello").Build();

        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"), defaultConfig);

        Assert.Collection(result.Messages,
            message =>
            {
                Assert.Equal("Hello", message.Content);
                Assert.Equal(Role.User, message.Role);
            });
        Assert.Equal(defaultConfig.Enabled, result.Enabled);
    }

    [Fact]
    public void CompletionConfigMethodCallsTrackWithCorrectParameters()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var context = Context.New(ContextKind.Default, "user-key");
        var configKey = "test-config-key";

        mockClient.Setup(c => c.JsonVariation(
                It.IsAny<string>(),
                It.IsAny<Context>(),
                It.IsAny<LdValue>()))
            .Returns(LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["variationKey"] = LdValue.Of("test-variation"),
                    ["version"] = LdValue.Of(1)
                }),
                ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["name"] = LdValue.Of("test-model")
                }),
                ["provider"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["name"] = LdValue.Of("test-provider")
                }),
                ["messages"] = LdValue.ArrayOf()
            }));

        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var defaultConfig = LdAiCompletionConfigDefault.New().Build();

        var result = client.CompletionConfig(configKey, context, defaultConfig);

        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:completion-config",
            context,
            LdValue.Of(configKey),
            1), Times.Once);

        Assert.NotNull(result);
        Assert.NotNull(result.CreateTracker());
    }

    [Fact]
    public void ConstructorTracksSdkInfo()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        Assert.NotNull(client);

        mockClient.Verify(c => c.Track(
            "$ld:ai:sdk:info",
            It.Is<Context>(ctx =>
                ctx.Kind == ContextKind.Of("ld_ai") &&
                ctx.Key == "ld-internal-tracking" &&
                ctx.Anonymous),
            It.Is<LdValue>(v =>
                v.Get("aiSdkName").AsString == SdkInfo.Name &&
                v.Get("aiSdkVersion").AsString == SdkInfo.Version &&
                v.Get("aiSdkLanguage").AsString == SdkInfo.Language),
            1), Times.Once);
    }

    private const string MetaDisabledExplicitly = """
                                                  {
                                                    "_ldMeta": {"variationKey": "1", "enabled": false},
                                                    "model": {},
                                                    "messages": []
                                                  }
                                                  """;

    private const string MetaDisabledImplicitly = """
                                                  {
                                                    "_ldMeta": {"variationKey": "1"},
                                                    "model": {},
                                                    "messages": []
                                                  }
                                                  """;

    private const string MissingMeta = """
                                       {
                                         "model": {},
                                         "messages": []
                                       }
                                       """;

    private const string EmptyObject = "{}";

    [Theory]
    [InlineData(MetaDisabledExplicitly)]
    [InlineData(MetaDisabledImplicitly)]
    [InlineData(MissingMeta)]
    [InlineData(EmptyObject)]
    public void ConfigNotEnabledReturnsDisabledInstance(string json)
    {
        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);

        // All the JSON inputs here are considered disabled, either due to lack of the 'enabled' property,
        // or if present, it is set to false. Therefore, if the default was returned, we'd see the assertion fail
        // (since calling LdAiCompletionConfigDefault.New() constructs an enabled config by default,
        // per AISDK spec Requirement 1.3.2).
        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiCompletionConfigDefault.New().AddMessage("foo").Build());

        Assert.False(result.Enabled);
    }

    [Fact]
    public void CanSetAllDefaultValueFields()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        // Mimic JsonVariation's real contract: on eval failure, return the supplied default.
        mockClient.Setup(x =>
                x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns((string _, Context _, LdValue dv) => dv);

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);

        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiCompletionConfigDefault.New().
                AddMessage("foo").
                SetModelParam("foo", LdValue.Of("bar")).
                SetModelName("awesome-model").
                SetCustomModelParam("foo", LdValue.Of("baz")).
                SetModelProviderName("amazing-provider").
                SetEnabled(true).Build());

        Assert.True(result.Enabled);
        Assert.Collection(result.Messages,
            message =>
            {
                Assert.Equal("foo", message.Content);
                Assert.Equal(Role.User, message.Role);
            });
        Assert.Equal("amazing-provider", result.Provider.Name);
        Assert.Equal("bar", result.Model.Parameters["foo"].AsString);
        Assert.Equal("baz", result.Model.Custom["foo"].AsString);
        Assert.Equal("awesome-model", result.Model.Name);
    }

    [Fact]
    public void ConfigEnabledReturnsInstance()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "1", "enabled": true},
                              "messages": [{"content": "Hello!", "role": "system"}]
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New(ContextKind.Default, "key");
        var client = new LdAiClient(mockClient.Object);

        // We shouldn't get this default.
        var result = client.CompletionConfig("foo", context,
            LdAiCompletionConfigDefault.New().AddMessage("Goodbye!").Build());

        Assert.Collection(result.Messages,
            message =>
            {
                Assert.Equal("Hello!", message.Content);
                Assert.Equal(Role.System, message.Role);
            });

        Assert.Equal("", result.Provider.Name);
        Assert.Equal("", result.Model.Name);
        Assert.Empty(result.Model.Custom);
        Assert.Empty(result.Model.Parameters);
    }


    [Fact]
    public void ModelParametersAreParsed()
    {

        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "1", "enabled": true},
                              "model" : {
                                "name": "model-foo",
                                "parameters": {
                                  "foo": "bar",
                                  "baz": 42
                                },
                                "custom": {
                                  "foo": "baz",
                                  "baz": 43
                                }
                              }
                            }
                            """;


        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New(ContextKind.Default, "key");
        var client = new LdAiClient(mockClient.Object);

        // We shouldn't get this default.
        var result = client.CompletionConfig("foo", context,
            LdAiCompletionConfigDefault.New().AddMessage("Goodbye!").Build());

        Assert.Equal("model-foo", result.Model.Name);
        Assert.Equal("bar", result.Model.Parameters["foo"].AsString);
        Assert.Equal(42, result.Model.Parameters["baz"].AsInt);
        Assert.Equal("baz", result.Model.Custom["foo"].AsString);
        Assert.Equal(43, result.Model.Custom["baz"].AsInt);
    }

    [Fact]
    public void ProviderConfigIsParsed()
    {

        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "1", "enabled": true},
                              "provider": {
                                "name": "amazing-provider"
                              }
                            }
                            """;


        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New(ContextKind.Default, "key");
        var client = new LdAiClient(mockClient.Object);

        // We shouldn't get this default.
        var result = client.CompletionConfig("foo", context,
            LdAiCompletionConfigDefault.New().AddMessage("Goodbye!").Build());

        Assert.Equal("amazing-provider", result.Provider.Name);
    }

    [Fact]
    public void ConfigWithoutDefaultValueUsesDisabledConfig()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Null);
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.Config("foo", Context.New(ContextKind.Default, "key"));

        Assert.False(result.Enabled);
    }

    [Fact]
    public void DisabledMethodReturnsDisabledConfig()
    {
        var config = LdAiCompletionConfigDefault.Disabled;
        Assert.False(config.Enabled);
    }

    [Fact]
    public void DisabledMethodReturnsNewInstanceEachCall()
    {
        var first = LdAiCompletionConfigDefault.Disabled;
        var second = LdAiCompletionConfigDefault.Disabled;
        Assert.NotSame(first, second);
    }

    [Fact]
    public void CompletionConfigReturnsDisabledWhenFlagModeMismatch()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        // A flag whose _ldMeta.mode is "agent" should not be served via the completion entry point.
        // The factory must log a warning and return a disabled config with a working tracker.
        const string agentJson = """
                                 {
                                     "_ldMeta": {"variationKey": "1", "enabled": true, "mode": "agent"},
                                     "model": { "name": "should-be-ignored" },
                                     "provider": { "name": "should-be-ignored" },
                                     "messages": [
                                         { "content": "should be ignored", "role": "system" }
                                     ]
                                 }
                                 """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(agentJson));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New("key"),
            LdAiCompletionConfigDefault.New().AddMessage("default").Build());

        Assert.False(result.Enabled);
        Assert.Empty(result.Messages);
        Assert.Equal("", result.Model.Name);
        Assert.Equal("", result.Provider.Name);

        // Tracker must still work — every config the SDK returns has a working tracker.
        var tracker = result.CreateTracker();
        Assert.NotNull(tracker);
        tracker.TrackSuccess();
        mockClient.Verify(x => x.Track("$ld:ai:generation:success", It.IsAny<Context>(), It.IsAny<LdValue>(), 1.0f), Times.Once);

        // The mismatch should produce a single warning log line. Substrings pin the shape
        // without locking surface rephrasings (quote style, exact punctuation).
        mockLogger.Verify(x => x.Warn(It.Is<string>(s =>
            s.Contains("AI Config mode mismatch for foo") &&
            s.Contains("expected completion") &&
            s.Contains("got agent") &&
            s.Contains("Returning disabled config"))), Times.Once);
    }
}
