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
            var tracker = new LdAiConfigTracker(null);
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


            var tracker = new LdAiConfigTracker(mockClient.Object);

            var config = tracker.GetModelConfig("foo", Context.New(ContextKind.Default, "key"), LdAiConfig.Default);

            Assert.Equal(config, LdAiConfig.Default);
        }
    }
}
