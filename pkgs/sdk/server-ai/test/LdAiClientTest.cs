using System.Collections.Generic;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai
{
    public class LdAiClientTest
    {
        [Fact]
        public void CanCallDispose()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            var tracker = new LdAiClient(mockClient.Object);
            tracker.Dispose();
        }

        [Fact]
        public void ThrowsIfClientIsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => new LdAiClient(null));
        }

        [Fact]
        public void ReturnsDefaultConfigWhenFlagNotFound()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();

            var mockLogger = new Mock<ILogger>();

            mockClient.Setup(x =>
                x.JsonVariationDetail("foo", It.IsAny<Context>(), LdValue.Null)).Returns(
                new EvaluationDetail<LdValue>(LdValue.Null, null, EvaluationReason.ErrorReason(EvaluationErrorKind.FlagNotFound)));


            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);


            var client = new LdAiClient(mockClient.Object);

            var defaultConfig = LdAiConfig.New().AddPromptMessage("Hello").Build();

            var tracker = client.GetModelConfig("foo", Context.New(ContextKind.Default, "key"), defaultConfig);

            Assert.Equal( defaultConfig, tracker.Config);
        }

        private const string MetaDisabledExplicitly = """
                                            {
                                              "_ldMeta": {"versionKey": "1", "enabled": false},
                                              "model": {},
                                              "prompt": []
                                            }
                                            """;

        private const string MetaDisabledImplicitly = """
                                                      {
                                                        "_ldMeta": {"versionKey": "1"},
                                                        "model": {},
                                                        "prompt": []
                                                      }
                                                      """;

        private const string MissingMeta = """
                                          {
                                            "model": {},
                                            "prompt": []
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
                x.JsonVariationDetail("foo", It.IsAny<Context>(), LdValue.Null)).Returns(
                new EvaluationDetail<LdValue>(LdValue.Parse(json), 0, EvaluationReason.FallthroughReason));

            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

            var client = new LdAiClient(mockClient.Object);

            // All the JSON inputs here are considered disabled, either due to lack of the 'enabled' property,
            // or if present, it is set to false. Therefore if the default was returned, we'd see the assertion fail
            // (since calling LdAiConfig.New() constructs an enabled config by default.)
            var tracker = client.GetModelConfig("foo", Context.New(ContextKind.Default, "key"),
                LdAiConfig.New().AddPromptMessage("foo").Build());



            Assert.False(tracker.Config.IsEnabled());
        }

        [Fact]
        public void ConfigEnabledReturnsInstance()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();

            var mockLogger = new Mock<ILogger>();

            const string json = """
                                {
                                  "_ldMeta": {"versionKey": "1", "enabled": true},
                                  "model": {},
                                  "prompt": [{"content": "Hello!", "role": "system"}]
                                }
                                """;

            mockClient.Setup(x =>
                x.JsonVariationDetail("foo", It.IsAny<Context>(), LdValue.Null)).Returns(
                new EvaluationDetail<LdValue>(LdValue.Parse(json), 0, EvaluationReason.FallthroughReason));

            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

            var context = Context.New(ContextKind.Default, "key");
            var client = new LdAiClient(mockClient.Object);

            // We shouldn't get this default.
            var tracker = client.GetModelConfig("foo", context,
                LdAiConfig.New().AddPromptMessage("Goodbye!").Build());

            Assert.Equal(new List<LdAiConfig.Message>{ new("Hello!", Role.System) }, tracker.Config.Prompt);
        }
    }
}
