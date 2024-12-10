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
        var result= aiClient.Config("foo", Context.New("key"), LdAiConfig.Disabled);
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

        var tracker = client.Config("foo", Context.New(ContextKind.Default, "key"), defaultConfig);

        Assert.Equal(defaultConfig, tracker.Config);
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
        // (since calling LdAiConfig.New() constructs an enabled config by default.)
        var tracker = client.Config("foo", Context.New(ContextKind.Default, "key"),
            LdAiConfig.New().AddMessage("foo").Build());

        Assert.False(tracker.Config.Enabled);
    }

    [Fact]
    public void CanSetAllDefaultValueFields()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();

        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Null);

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);

        var tracker = client.Config("foo", Context.New(ContextKind.Default, "key"),
            LdAiConfig.New().
                AddMessage("foo").
                SetModelParam("foo", LdValue.Of("bar")).
                SetModelName("awesome-model").
                SetCustomModelParam("foo", LdValue.Of("baz")).
                SetModelProviderName("amazing-provider").
                SetEnabled(true).Build());

        Assert.True(tracker.Config.Enabled);
        Assert.Collection(tracker.Config.Messages,
            message =>
            {
                Assert.Equal("foo", message.Content);
                Assert.Equal(Role.User, message.Role);
            });
        Assert.Equal("amazing-provider", tracker.Config.Provider.Name);
        Assert.Equal("bar", tracker.Config.Model.Parameters["foo"].AsString);
        Assert.Equal("baz", tracker.Config.Model.Custom["foo"].AsString);
        Assert.Equal("awesome-model", tracker.Config.Model.Name);
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
        var tracker = client.Config("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Collection(tracker.Config.Messages,
            message =>
            {
                Assert.Equal("Hello!", message.Content);
                Assert.Equal(Role.System, message.Role);
            });

        Assert.Equal("", tracker.Config.Provider.Name);
        Assert.Equal("", tracker.Config.Model.Name);
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
        var tracker = client.Config("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Equal("model-foo", tracker.Config.Model.Name);
        Assert.Equal("bar", tracker.Config.Model.Parameters["foo"].AsString);
        Assert.Equal(42, tracker.Config.Model.Parameters["baz"].AsInt);
        Assert.Equal("baz", tracker.Config.Model.Custom["foo"].AsString);
        Assert.Equal(43, tracker.Config.Model.Custom["baz"].AsInt);
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
        var tracker = client.Config("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Equal("amazing-provider", tracker.Config.Provider.Name);
    }
}
