using LaunchDarkly.Sdk.Server.Ai.Config;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai
{
    public class LdAiTrackerTest
    {
        [Fact]
        public void ThrowsIfClientIsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => new LdAiConfigTracker(null, LdAiConfig.Disabled, Context.New("key"), "key"));
        }

        [Fact]
        public void ThrowsIfConfigIsNull()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            Assert.Throws<System.ArgumentNullException>(() => new LdAiConfigTracker(mockClient.Object, null, Context.New("key"), "key"));
        }

        [Fact]
        public void ThrowsIfKeyIsNull()
        {
            var mockClient = new Mock<ILaunchDarklyClient>();
            Assert.Throws<System.ArgumentNullException>(() => new LdAiConfigTracker(mockClient.Object, LdAiConfig.Disabled, Context.New("key"), null));
        }
    }
}
