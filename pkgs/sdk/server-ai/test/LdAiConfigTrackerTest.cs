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
    }
}
