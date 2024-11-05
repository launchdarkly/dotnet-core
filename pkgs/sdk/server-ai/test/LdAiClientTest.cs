using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Ai.Config;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai
{
    public class LdAiTrackerTest
    {
        [Fact]
        public void CanCallDispose()
        {
            var tracker = new LdAiClient(null);
            tracker.Dispose();
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


            var tracker = new LdAiClient(mockClient.Object);

            var config = tracker.GetModelConfig("foo", Context.New(ContextKind.Default, "key"), LdAiConfig.Default);

            Assert.Equal(config, LdAiConfig.Default);
        }

        private const string MetaDisabledExplicitly = """
                                            {
                                              "_ldMeta": {"versionKey": 1, "enabled": false},
                                              "model": {},
                                              "prompt": []
                                            }
                                            """;

        private const string MetaDisabledImplicitly = """
                                                      {
                                                        "_ldMeta": {"versionKey": 1},
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
        // Each of these JSON strings represents a possible configuration that should result in
        // an AiConfig.Disabled instance.
        public void ConfigNotEnabledReturnsDisabledInstance(string json)
        {
            var mockClient = new Mock<ILaunchDarklyClient>();

            var mockLogger = new Mock<ILogger>();

            mockClient.Setup(x =>
                x.JsonVariationDetail("foo", It.IsAny<Context>(), LdValue.Null)).Returns(
                new EvaluationDetail<LdValue>(LdValue.Parse(json), 0, EvaluationReason.FallthroughReason));

            mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

            var tracker = new LdAiClient(mockClient.Object);
            var config = tracker.GetModelConfig("foo", Context.New(ContextKind.Default, "key"), null);

            Assert.Equal(config, LdAiConfig.Disabled);
        }
    }
}
