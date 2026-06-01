using System.Reflection;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class LdAiCompletionConfigTest
{
    [Fact]
    public void LdAiCompletionConfigHasNoPublicConstructors()
    {
        // Locks in the invariant that users cannot directly construct an LdAiCompletionConfig.
        // It is only produced by LdAiClient.CompletionConfig, which guarantees a working
        // tracker factory is wired up.
        var ctors = typeof(LdAiCompletionConfig).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(ctors);
    }

    [Fact]
    public void CreateTrackerIsNonNullOnParseFailureFallback()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        // Returning LdValue.Null causes ParseConfig to fail, triggering the parse-failure
        // fallback path in LdAiClient.Evaluate.
        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Null);
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New("key"),
            LdAiCompletionConfigDefault.New().AddMessage("Hello").Build());

        var tracker = result.CreateTracker();
        Assert.NotNull(tracker);
        // Calling a method on the tracker should not throw; this proves the tracker is wired
        // to the underlying client.
        tracker.TrackSuccess();
        mockClient.Verify(x => x.Track("$ld:ai:generation:success", It.IsAny<Context>(), It.IsAny<LdValue>(), 1.0f), Times.Once);
    }

    [Fact]
    public void CreateTrackerIsNonNullOnInterpolationFailureFallback()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        // A malformed Mustache template causes the template Compile step to throw inside the
        // message loop, triggering the interpolation-failure fallback path in LdAiClient.Evaluate.
        const string malformedJson = """
                                     {
                                         "_ldMeta": {"variationKey": "1", "enabled": true},
                                         "model": {},
                                         "messages": [
                                             {
                                                 "content": "This is a {{ malformed }]} prompt",
                                                 "role": "System"
                                             }
                                         ]
                                     }
                                     """;

        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Parse(malformedJson));
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New("key"),
            LdAiCompletionConfigDefault.New().AddMessage("Hello").Build());

        var tracker = result.CreateTracker();
        Assert.NotNull(tracker);
        tracker.TrackSuccess();
        mockClient.Verify(x => x.Track("$ld:ai:generation:success", It.IsAny<Context>(), It.IsAny<LdValue>(), 1.0f), Times.Once);
    }
}
