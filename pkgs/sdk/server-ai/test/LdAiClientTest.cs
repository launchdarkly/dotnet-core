using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
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
        var result = aiClient.CompletionConfig("foo", Context.New("key"), LdAiConfig.Disabled);
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

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Null);


        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);


        var client = new LdAiClient(mockClient.Object);

        var defaultConfig = LdAiConfig.New().AddMessage("Hello").Build();

        var config = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"), defaultConfig);

        Assert.Equal(defaultConfig.Enabled, config.Enabled);
        Assert.Equal(defaultConfig.Messages, config.Messages);
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
        var defaultConfig = LdAiConfig.New().Build();

        var config = client.CompletionConfig(configKey, context, defaultConfig);

        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:completion-config",
            context,
            LdValue.Of(configKey),
            1), Times.Once);

        Assert.NotNull(config);
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
        // (since calling LdAiConfig.New() constructs an enabled config by default.)
        var config = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiConfig.New().AddMessage("foo").Build());

        Assert.False(config.Enabled);
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

        var config = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiConfig.New().
                AddMessage("foo").
                SetModelParam("foo", LdValue.Of("bar")).
                SetModelName("awesome-model").
                SetCustomModelParam("foo", LdValue.Of("baz")).
                SetModelProviderName("amazing-provider").
                SetEnabled(true).Build());

        Assert.True(config.Enabled);
        Assert.Collection(config.Messages,
            message =>
            {
                Assert.Equal("foo", message.Content);
                Assert.Equal(Role.User, message.Role);
            });
        Assert.Equal("amazing-provider", config.Provider.Name);
        Assert.Equal("bar", config.Model.Parameters["foo"].AsString);
        Assert.Equal("baz", config.Model.Custom["foo"].AsString);
        Assert.Equal("awesome-model", config.Model.Name);
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
        var config = client.CompletionConfig("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Collection(config.Messages,
            message =>
            {
                Assert.Equal("Hello!", message.Content);
                Assert.Equal(Role.System, message.Role);
            });

        Assert.Equal("", config.Provider.Name);
        Assert.Equal("", config.Model.Name);
        Assert.Empty(config.Model.Custom);
        Assert.Empty(config.Model.Parameters);
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
        var config = client.CompletionConfig("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Equal("model-foo", config.Model.Name);
        Assert.Equal("bar", config.Model.Parameters["foo"].AsString);
        Assert.Equal(42, config.Model.Parameters["baz"].AsInt);
        Assert.Equal("baz", config.Model.Custom["foo"].AsString);
        Assert.Equal(43, config.Model.Custom["baz"].AsInt);
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
        var config = client.CompletionConfig("foo", context,
            LdAiConfig.New().AddMessage("Goodbye!").Build());

        Assert.Equal("amazing-provider", config.Provider.Name);
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
        var config = client.Config("foo", Context.New(ContextKind.Default, "key"));

        Assert.False(config.Enabled);
    }

    [Fact]
    public void DisabledMethodReturnsDisabledConfig()
    {
        var config = LdAiConfig.Disabled;
        Assert.False(config.Enabled);
    }

    [Fact]
    public void DisabledMethodReturnsNewInstanceEachCall()
    {
        var first = LdAiConfig.Disabled;
        var second = LdAiConfig.Disabled;
        Assert.NotSame(first, second);
    }

    [Fact]
    public void CompletionConfigReturnsConfigWithCreateTrackerFactory()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");
        const string configKey = "my-config";

        mockClient.Setup(x =>
            x.JsonVariation(configKey, It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(
            LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["variationKey"] = LdValue.Of("var-1"),
                    ["version"] = LdValue.Of(3)
                }),
                ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["name"] = LdValue.Of("gpt-4")
                }),
                ["provider"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["name"] = LdValue.Of("openai")
                }),
                ["messages"] = LdValue.ArrayOf()
            }));

        var client = new LdAiClient(mockClient.Object);
        var config = client.CompletionConfig(configKey, context);

        Assert.NotNull(config.CreateTracker);

        var tracker = config.CreateTracker();
        Assert.NotNull(tracker);
    }

    [Fact]
    public void CreateTrackerFactoryReturnsTrackersWithFreshRunIds()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");
        const string configKey = "my-config";

        mockClient.Setup(x =>
            x.JsonVariation(configKey, It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(
            LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["variationKey"] = LdValue.Of("var-1"),
                    ["version"] = LdValue.Of(3)
                }),
                ["messages"] = LdValue.ArrayOf()
            }));

        var client = new LdAiClient(mockClient.Object);
        var config = client.CompletionConfig(configKey, context);

        var tracker1 = config.CreateTracker();
        var tracker2 = config.CreateTracker();

        tracker1.TrackDuration(10.0f);
        tracker2.TrackDuration(20.0f);

        string runId1 = null;
        string runId2 = null;

        foreach (var call in mockClient.Invocations)
        {
            if (call.Method.Name == "Track" && (string)call.Arguments[0] == "$ld:ai:duration:total")
            {
                var data = (LdValue)call.Arguments[2];
                var runId = data.Get("runId").AsString;
                if (runId1 == null) runId1 = runId;
                else runId2 = runId;
            }
        }

        Assert.NotNull(runId1);
        Assert.NotNull(runId2);
        Assert.NotEqual(runId1, runId2);
    }

    [Fact]
    public void CreateTrackerFactoryReturnsTrackersWithIndependentState()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");
        const string configKey = "my-config";

        mockClient.Setup(x =>
            x.JsonVariation(configKey, It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(
            LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["variationKey"] = LdValue.Of("var-1"),
                    ["version"] = LdValue.Of(3)
                }),
                ["messages"] = LdValue.ArrayOf()
            }));

        var client = new LdAiClient(mockClient.Object);
        var config = client.CompletionConfig(configKey, context);

        var tracker1 = config.CreateTracker();
        var tracker2 = config.CreateTracker();

        // Both should be able to track independently
        tracker1.TrackSuccess();
        tracker2.TrackSuccess();

        mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
            It.IsAny<LdValue>(), It.IsAny<double>()), Times.Exactly(2));
    }

    [Fact]
    public void DefaultConfigGetsCreateTrackerFactory()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Null);
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var config = client.CompletionConfig("foo", Context.New("key"));

        Assert.NotNull(config.CreateTracker);

        var tracker = config.CreateTracker();
        Assert.NotNull(tracker);
    }

    [Fact]
    public void CreateTrackerFromResumptionTokenRoundTrips()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");
        const string configKey = "my-config";

        mockClient.Setup(x =>
            x.JsonVariation(configKey, It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(
            LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                ["_ldMeta"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["enabled"] = LdValue.Of(true),
                    ["variationKey"] = LdValue.Of("var-1"),
                    ["version"] = LdValue.Of(3)
                }),
                ["model"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["name"] = LdValue.Of("gpt-4")
                }),
                ["provider"] = LdValue.ObjectFrom(new Dictionary<string, LdValue>
                {
                    ["name"] = LdValue.Of("openai")
                }),
                ["messages"] = LdValue.ArrayOf()
            }));

        var client = new LdAiClient(mockClient.Object);
        var config = client.CompletionConfig(configKey, context);
        var originalTracker = config.CreateTracker();
        var token = originalTracker.ResumptionToken;

        // Reconstruct in a different context
        var newContext = Context.New("other-user");
        var resumedTracker = client.CreateTracker(token, newContext);

        Assert.NotNull(resumedTracker);

        // Track on both and verify the resumed tracker uses the same runId
        originalTracker.TrackDuration(100);
        resumedTracker.TrackDuration(200);

        string originalRunId = null;
        string resumedRunId = null;

        foreach (var call in mockClient.Invocations)
        {
            if (call.Method.Name == "Track" && (string)call.Arguments[0] == "$ld:ai:duration:total")
            {
                var data = (LdValue)call.Arguments[2];
                var runId = data.Get("runId").AsString;
                if (originalRunId == null) originalRunId = runId;
                else resumedRunId = runId;
            }
        }

        Assert.NotNull(originalRunId);
        Assert.NotNull(resumedRunId);
        Assert.Equal(originalRunId, resumedRunId);
    }

    [Fact]
    public void CreateTrackerFromResumptionTokenSetsEmptyModelAndProvider()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");

        // Build a token manually with known values
        var payload = JsonSerializer.Serialize(new
        {
            runId = "test-run-id",
            configKey = "test-key",
            variationKey = "var-1",
            version = 2,
        });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var token = base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var client = new LdAiClient(mockClient.Object);
        var tracker = client.CreateTracker(token, context);

        // Track something and verify the track data has empty model/provider
        tracker.TrackSuccess();

        mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
            It.Is<LdValue>(d =>
                d.Get("runId").AsString == "test-run-id" &&
                d.Get("configKey").AsString == "test-key" &&
                d.Get("variationKey").AsString == "var-1" &&
                d.Get("version").AsInt == 2 &&
                d.Get("modelName").AsString == "" &&
                d.Get("providerName").AsString == ""),
            1.0f), Times.Once);
    }

    [Fact]
    public void CreateTrackerFromInvalidTokenThrows()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var context = Context.New("user-key");

        Assert.Throws<ArgumentException>(() => client.CreateTracker("not-valid-base64!!!", context));
    }

    [Fact]
    public void CreateTrackerFromNullTokenThrows()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var context = Context.New("user-key");

        Assert.Throws<ArgumentNullException>(() => client.CreateTracker(null, context));
    }

    [Fact]
    public void CreateTrackerFromTokenMissingRunIdThrows()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var context = Context.New("user-key");

        var payload = JsonSerializer.Serialize(new
        {
            configKey = "test-key",
            variationKey = "var-1",
            version = 1,
        });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var token = base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.Throws<ArgumentException>(() => client.CreateTracker(token, context));
    }

    [Fact]
    public void CreateTrackerFromTokenWithoutVariationKeyHandlesAbsence()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");

        // Token without variationKey
        var payload = JsonSerializer.Serialize(new
        {
            runId = "test-run-id",
            configKey = "test-key",
            version = 3,
        });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var token = base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var client = new LdAiClient(mockClient.Object);
        var tracker = client.CreateTracker(token, context);

        Assert.NotNull(tracker);

        // Track and verify variationKey is null in the track data
        tracker.TrackSuccess();

        mockClient.Verify(x => x.Track("$ld:ai:generation:success", context,
            It.Is<LdValue>(d =>
                d.Get("runId").AsString == "test-run-id" &&
                d.Get("configKey").AsString == "test-key" &&
                d.Get("variationKey").IsNull &&
                d.Get("version").AsInt == 3),
            1.0f), Times.Once);
    }
}
