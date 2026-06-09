using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
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
                Assert.Equal(LdAiConfigTypes.Role.User, message.Role);
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
                Assert.Equal(LdAiConfigTypes.Role.User, message.Role);
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
                Assert.Equal(LdAiConfigTypes.Role.System, message.Role);
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
    public void ReturnsCallerDefaultWhenFlagModeMismatch()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        // A flag whose _ldMeta.mode is "agent" should not be served via the completion entry
        // point. The factory logs a warning and returns the caller's default with a working
        // tracker (per sdk-specs PR #229).
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
        var defaultConfig = LdAiCompletionConfigDefault.New().AddMessage("default").Build();
        var result = client.CompletionConfig("foo", Context.New("key"), defaultConfig);

        // Result reflects the caller's default rather than a synthetic disabled config.
        Assert.True(result.Enabled);
        Assert.Collection(result.Messages,
            message =>
            {
                Assert.Equal("default", message.Content);
                Assert.Equal(LdAiConfigTypes.Role.User, message.Role);
            });
        Assert.Equal(defaultConfig.Model.Name, result.Model.Name);
        Assert.Equal(defaultConfig.Provider.Name, result.Provider.Name);

        // Tracker must still work — every config the SDK returns has a working tracker.
        var tracker = result.CreateTracker();
        Assert.NotNull(tracker);
        tracker.TrackSuccess();
        mockClient.Verify(x => x.Track("$ld:ai:generation:success", It.IsAny<Context>(), It.IsAny<LdValue>(), 1.0f), Times.Once);

        // Format string carries the structural shape; positional args carry the values.
        // We verify both so a regression in either piece is caught.
        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s =>
                s.Contains("AI Config mode mismatch") &&
                s.Contains("Returning caller's default")),
            It.Is<object[]>(args =>
                args.Length == 3 &&
                (string)args[0] == "foo" &&
                (string)args[1] == "completion" &&
                (string)args[2] == "agent")
        ), Times.Once);
    }

    [Fact]
    public void ReturnsDefaultConfigWhenFlagIsNotAnAiConfig()
    {
        // Spec Req 1.2.3.4 step 2: a non-object variation result must be replaced with
        // the caller's default, with an error log. A flag mistyped as a bare number
        // reaches the factory as a non-object LdValue.
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Of(42));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var defaultConfig = LdAiCompletionConfigDefault.New().AddMessage("Hello").Build();
        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "user-key"), defaultConfig);

        Assert.True(result.Enabled);
        Assert.Collection(result.Messages,
            message =>
            {
                Assert.Equal("Hello", message.Content);
                Assert.Equal(LdAiConfigTypes.Role.User, message.Role);
            });

        mockLogger.Verify(x => x.Error(
            It.Is<string>(s =>
                s.Contains("AI Config") &&
                s.Contains("is not an object") &&
                s.Contains("using caller's default")),
            It.Is<object[]>(args =>
                args.Length == 2 &&
                (string)args[0] == "foo" &&
                (LdValueType)args[1] == LdValueType.Number)
        ), Times.Once);
    }

    [Fact]
    public void LdCtxOverrideIsSilent()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "1", "enabled": true},
                              "messages": [{"content": "Hello {{ldctx.key}}", "role": "system"}]
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);

        // Supply a user-provided ldctx that should be silently overridden by the SDK's context.
        var variables = new Dictionary<string, object>
        {
            ["ldctx"] = new Dictionary<string, object> { ["key"] = "user-supplied-value" }
        };

        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "sdk-key"), null, variables);

        // The SDK context's key ("sdk-key") wins, not the user-supplied one.
        Assert.Collection(result.Messages,
            m => Assert.Equal("Hello sdk-key", m.Content));

        // No warning should be logged about the ldctx key.
        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s => s.Contains("ldctx")),
            It.IsAny<object[]>()
        ), Times.Never);
    }

    [Fact]
    public void ToolsPopulatedOnCompletionConfig()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "1", "enabled": true},
                              "model": {},
                              "messages": [],
                              "tools": {
                                "search": {
                                  "name": "search",
                                  "description": "Searches the web",
                                  "type": "function",
                                  "parameters": {"query": "string"},
                                  "customParameters": {"timeout": 30}
                                }
                              }
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiCompletionConfigDefault.Disabled);

        Assert.Single(result.Tools);
        Assert.True(result.Tools.ContainsKey("search"));
        Assert.Equal("search", result.Tools["search"].Name);
        Assert.Equal("Searches the web", result.Tools["search"].Description);
        Assert.Equal(30, result.Tools["search"].CustomParameters["timeout"].AsInt);
    }

    [Fact]
    public void ToolsEmptyWhenAbsentFromJson()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "1", "enabled": true},
                              "model": {},
                              "messages": []
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiCompletionConfigDefault.Disabled);

        Assert.NotNull(result.Tools);
        Assert.Empty(result.Tools);
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

    [Fact]
    public void JudgeConfigurationPopulatedOnCompletionConfig()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "1", "enabled": true},
                              "model": {},
                              "messages": [],
                              "judgeConfiguration": {
                                "judges": [
                                  {"key": "accuracy", "samplingRate": 0.5},
                                  {"key": "relevance", "samplingRate": 1.0}
                                ]
                              }
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiCompletionConfigDefault.Disabled);

        Assert.NotNull(result.JudgeConfiguration);
        Assert.Equal(2, result.JudgeConfiguration.Judges.Count);
        Assert.Equal("accuracy", result.JudgeConfiguration.Judges[0].Key);
        Assert.Equal(0.5, result.JudgeConfiguration.Judges[0].SamplingRate);
        Assert.Equal("relevance", result.JudgeConfiguration.Judges[1].Key);
        Assert.Equal(1.0, result.JudgeConfiguration.Judges[1].SamplingRate);
    }

    [Fact]
    public void JudgeConfigurationNullWhenAbsent()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "1", "enabled": true},
                              "model": {},
                              "messages": []
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"),
            LdAiCompletionConfigDefault.Disabled);

        Assert.Null(result.JudgeConfiguration);
    }

    [Fact]
    public void CompletionConfigDefaultWithJudgeConfiguration()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns((string _, Context _, LdValue dv) => dv);
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var judgeConfig = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>
            {
                new LdAiConfigTypes.JudgeConfiguration.Judge("precision", 0.8)
            });

        var defaultConfig = LdAiCompletionConfigDefault.New()
            .SetJudgeConfiguration(judgeConfig)
            .Build();

        Assert.NotNull(defaultConfig.JudgeConfiguration);
        Assert.Single(defaultConfig.JudgeConfiguration.Judges);
        Assert.Equal("precision", defaultConfig.JudgeConfiguration.Judges[0].Key);
        Assert.Equal(0.8, defaultConfig.JudgeConfiguration.Judges[0].SamplingRate);
    }

    [Fact]
    public void AgentConfig_BasicRetrieval()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

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

        mockClient.Setup(x =>
            x.JsonVariation("agent-key", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.AgentConfig("agent-key", Context.New("user"));

        Assert.Equal("agent-key", result.Key);
        Assert.True(result.Enabled);
        Assert.Equal("You are a helpful assistant", result.Instructions);
        Assert.Equal("gpt-4o", result.Model.Name);
        Assert.Equal("openai", result.Provider.Name);
        Assert.Single(result.Tools);
        Assert.True(result.Tools.ContainsKey("search"));
        Assert.NotNull(result.CreateTracker());
    }

    [Fact]
    public void AgentConfig_ModeMismatch_ReturnsCallerDefault()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "completion"},
                              "model": {"name": "should-be-ignored"},
                              "instructions": "should be ignored"
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("agent-key", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var defaultConfig = LdAiAgentConfigDefault.New()
            .SetInstructions("default instructions")
            .SetModelName("default-model")
            .Build();

        var result = client.AgentConfig("agent-key", Context.New("user"), defaultConfig);

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
    public void AgentConfig_UsageEventFired()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {},
                              "provider": {},
                              "instructions": "Be helpful"
                            }
                            """;

        var context = Context.New("user-key");
        mockClient.Setup(x =>
            x.JsonVariation("my-agent", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        client.AgentConfig("my-agent", context);

        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:agent-config",
            context,
            LdValue.Of("my-agent"),
            1), Times.Once);
    }

    [Fact]
    public void AgentConfigs_BatchEvaluatesEachKey()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation("agent-a", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse("""
                {
                  "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                  "model": {"name": "gpt-4o"},
                  "provider": {"name": "openai"},
                  "instructions": "Agent A"
                }
                """));

        mockClient.Setup(x =>
            x.JsonVariation("agent-b", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse("""
                {
                  "_ldMeta": {"variationKey": "v2", "enabled": true, "mode": "agent"},
                  "model": {"name": "claude-3"},
                  "provider": {"name": "anthropic"},
                  "instructions": "Agent B"
                }
                """));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");
        var client = new LdAiClient(mockClient.Object);

        var requests = new List<AgentConfigRequest>
        {
            new AgentConfigRequest { Key = "agent-a" },
            new AgentConfigRequest { Key = "agent-b" }
        };

        var result = client.AgentConfigs(requests, context);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("agent-a"));
        Assert.True(result.ContainsKey("agent-b"));
        Assert.Equal("Agent A", result["agent-a"].Instructions);
        Assert.Equal("Agent B", result["agent-b"].Instructions);
    }

    [Fact]
    public void AgentConfigs_FiresOnlyAggregateEvent_NotIndividualEvents()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation(It.IsAny<string>(), It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse("""
                {
                  "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                  "model": {},
                  "provider": {},
                  "instructions": "Be helpful"
                }
                """));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");
        var client = new LdAiClient(mockClient.Object);

        var requests = new List<AgentConfigRequest>
        {
            new AgentConfigRequest { Key = "agent-a" },
            new AgentConfigRequest { Key = "agent-b" }
        };

        client.AgentConfigs(requests, context);

        // Individual $ld:ai:usage:agent-config must NOT fire — the caller used AgentConfigs, not AgentConfig.
        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:agent-config",
            context,
            It.IsAny<LdValue>(),
            It.IsAny<double>()), Times.Never);

        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:agent-configs",
            context,
            LdValue.Of(2),
            2), Times.Once);
    }

    [Fact]
    public void AgentConfigs_DuplicateKeys_AggregateEventCountsAllRequests()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        mockClient.Setup(x =>
            x.JsonVariation(It.IsAny<string>(), It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse("""
                {
                  "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                  "model": {},
                  "provider": {},
                  "instructions": "Be helpful"
                }
                """));

        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var context = Context.New("user-key");
        var client = new LdAiClient(mockClient.Object);

        var requests = new List<AgentConfigRequest>
        {
            new AgentConfigRequest { Key = "agent-a" },
            new AgentConfigRequest { Key = "agent-a" },
            new AgentConfigRequest { Key = "agent-b" }
        };

        var result = client.AgentConfigs(requests, context);

        // The result dictionary de-duplicates by key.
        Assert.Equal(2, result.Count);

        // Individual events must NOT fire.
        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:agent-config",
            context,
            It.IsAny<LdValue>(),
            It.IsAny<double>()), Times.Never);

        // Aggregate event counts all 3 requests, including the duplicate.
        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:agent-configs",
            context,
            LdValue.Of(3),
            3), Times.Once);
    }

    [Fact]
    public void JudgeConfig_BasicRetrieval()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

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

        mockClient.Setup(x =>
            x.JsonVariation("judge-key", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.JudgeConfig("judge-key", Context.New("user"));

        Assert.Equal("judge-key", result.Key);
        Assert.True(result.Enabled);
        Assert.Equal("$ld:ai:judge:relevance", result.EvaluationMetricKey);
        Assert.Equal("gpt-4o", result.Model.Name);
        Assert.Equal("openai", result.Provider.Name);
        Assert.Single(result.Messages);
        Assert.Equal("Rate the response", result.Messages[0].Content);
        Assert.Equal(LdAiConfigTypes.Role.System, result.Messages[0].Role);
        Assert.NotNull(result.CreateTracker());
    }

    [Fact]
    public void JudgeConfig_ModeMismatch_ReturnsCallerDefault()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {"name": "should-be-ignored"},
                              "messages": [{"content": "should be ignored", "role": "user"}]
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("judge-key", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var defaultConfig = LdAiJudgeConfigDefault.New()
            .AddMessage("default message")
            .SetModelName("default-model")
            .Build();

        var result = client.JudgeConfig("judge-key", Context.New("user"), defaultConfig);

        Assert.True(result.Enabled);
        Assert.Equal("default-model", result.Model.Name);
        Assert.Single(result.Messages);
        Assert.Equal("default message", result.Messages[0].Content);

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
    public void JudgeConfig_UsageEventFired()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "judge"},
                              "model": {},
                              "provider": {},
                              "messages": [],
                              "evaluationMetricKey": "$ld:ai:judge:relevance"
                            }
                            """;

        var context = Context.New("user-key");
        mockClient.Setup(x =>
            x.JsonVariation("my-judge", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        client.JudgeConfig("my-judge", context);

        mockClient.Verify(c => c.Track(
            "$ld:ai:usage:judge-config",
            context,
            LdValue.Of("my-judge"),
            1), Times.Once);
    }

    [Fact]
    public void AgentConfig_LdCtxInterpolatedInInstructions()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        const string json = """
                            {
                              "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "agent"},
                              "model": {},
                              "provider": {},
                              "instructions": "Hello {{ldctx.key}}"
                            }
                            """;

        mockClient.Setup(x =>
            x.JsonVariation("agent-key", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(json));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.AgentConfig("agent-key", Context.New("sdk-key"));

        Assert.Equal("Hello sdk-key", result.Instructions);
    }

    [Fact]
    public void CompletionConfigDefaultJudgeConfigurationSurvivesToLdValueRoundtrip()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        // Return the serialized default value so that BuildCompletionConfig parses it via
        // ParseJudgeConfiguration — this exercises the ToLdValue() → parse roundtrip.
        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns((string _, Context _, LdValue dv) => dv);
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var judgeConfig = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>
            {
                new LdAiConfigTypes.JudgeConfiguration.Judge("precision", 0.8)
            });

        var defaultConfig = LdAiCompletionConfigDefault.New()
            .SetJudgeConfiguration(judgeConfig)
            .Build();

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"), defaultConfig);

        Assert.NotNull(result.JudgeConfiguration);
        Assert.Single(result.JudgeConfiguration.Judges);
        Assert.Equal("precision", result.JudgeConfiguration.Judges[0].Key);
        Assert.Equal(0.8, result.JudgeConfiguration.Judges[0].SamplingRate);
    }

    [Fact]
    public void CompletionConfigDefaultJudgeConfigurationSurvivesBuildFromDefault()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        // Return null so the factory falls back to BuildCompletionFromDefault, preserving
        // the JudgeConfiguration from the caller-supplied default.
        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Null);
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var judgeConfig = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>
            {
                new LdAiConfigTypes.JudgeConfiguration.Judge("coherence", 0.3)
            });

        var defaultConfig = LdAiCompletionConfigDefault.New()
            .SetJudgeConfiguration(judgeConfig)
            .Build();

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New(ContextKind.Default, "key"), defaultConfig);

        Assert.NotNull(result.JudgeConfiguration);
        Assert.Single(result.JudgeConfiguration.Judges);
        Assert.Equal("coherence", result.JudgeConfiguration.Judges[0].Key);
        Assert.Equal(0.3, result.JudgeConfiguration.Judges[0].SamplingRate);
    }
}
