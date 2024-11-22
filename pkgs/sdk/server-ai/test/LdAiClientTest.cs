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
        var result= aiClient.ModelConfig("foo", Context.New("key"), LdAiConfig.Disabled);
        Assert.False(result.Config.Enabled);
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

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Null);


        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);


        var client = new LdAiClient(mockClient.Object);

        var defaultConfig = LdAiConfig.New().AddMessage("Hello").Build();

        var tracker = client.ModelConfig("foo", Context.New(ContextKind.Default, "key"), defaultConfig);

        Assert.Equal(defaultConfig, tracker.Config);
    }

    private const string MetaDisabledExplicitly = """
                                                  {
                                                    "_ldMeta": {"versionKey": "1", "enabled": false},
                                                    "model": {},
                                                    "messages": []
                                                  }
                                                  """;

    private const string MetaDisabledImplicitly = """
                                                  {
                                                    "_ldMeta": {"versionKey": "1"},
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
        // (since calling LdAiConfig.New() constructs an enabled config by default.)
        var tracker = client.ModelConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiConfig.New().AddMessage("foo").Build());

        Assert.False(tracker.Config.Enabled);
    }

    [Fact]
    public void ConfigEnabledReturnsInstance()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"versionKey": "1", "enabled": true},
                              "messages": [{"content": "Hello!", "role": "system"}]
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New(ContextKind.Default, "key");
        var client = new LdAiClient(mockClient.Object);

        // We shouldn't get this default.
        var tracker = client.ModelConfig("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Collection(tracker.Config.Messages,
            message =>
            {
                Assert.Equal("Hello!", message.Content);
                Assert.Equal(Role.System, message.Role);
            });

        Assert.Equal("", tracker.Config.Provider.Id);
        Assert.Equal("", tracker.Config.Model.Id);
        Assert.Empty(tracker.Config.Model.Custom);
        Assert.Empty(tracker.Config.Model.Parameters);
    }


    [Fact]
    public void ModelParametersAreParsed()
    {

        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"versionKey": "1", "enabled": true},
                              "model" : {
                                "id": "model-foo",
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
        var tracker = client.ModelConfig("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Equal("model-foo", tracker.Config.Model.Id);
        Assert.Equal(LdValue.Of("bar"), tracker.Config.Model.Parameters["foo"]);
        Assert.Equal(LdValue.Of(42), tracker.Config.Model.Parameters["baz"]);
        Assert.Equal(LdValue.Of("baz"), tracker.Config.Model.Custom["foo"]);
        Assert.Equal(LdValue.Of(43), tracker.Config.Model.Custom["baz"]);
    }

    [Fact]
    public void ProviderConfigIsParsed()
    {

        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"versionKey": "1", "enabled": true},
                              "provider": {
                                "id": "amazing-provider"
                              }
                            }
                            """;


        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New(ContextKind.Default, "key");
        var client = new LdAiClient(mockClient.Object);

        // We shouldn't get this default.
        var tracker = client.ModelConfig("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Equal("amazing-provider", tracker.Config.Provider.Id);
    }
}
