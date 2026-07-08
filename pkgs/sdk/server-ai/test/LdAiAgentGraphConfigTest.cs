using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// Tests graphKey threading through LdAiConfigTracker (spec tests 1–3).
/// </summary>
public class LdAiAgentGraphConfigTest
{
    private static Mock<ILaunchDarklyClient> MockClient()
    {
        var mock = new Mock<ILaunchDarklyClient>();
        mock.Setup(c => c.GetLogger()).Returns(new Mock<ILogger>().Object);
        return mock;
    }

    private static LdAiConfigTracker MakeTracker(ILaunchDarklyClient client, Context context,
        string graphKey = null)
    {
        return new LdAiConfigTracker(client, Guid.NewGuid().ToString(), "config-key",
            "v1", 1, context, "model", "provider", graphKey: graphKey);
    }

    // Test 1: Tracker created with graphKey → events include graphKey in data
    [Fact]
    public void TrackDataIncludesGraphKeyWhenSet()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context, "my-graph");

        tracker.TrackSuccess();

        mockClient.Verify(c => c.Track(
            It.IsAny<string>(),
            context,
            It.Is<LdValue>(v => v.Get("graphKey").AsString == "my-graph"),
            It.IsAny<double>()), Times.Once);
    }

    // Test 2: Tracker created without graphKey → events omit graphKey
    [Fact]
    public void TrackDataOmitsGraphKeyWhenNotSet()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context, graphKey: null);

        tracker.TrackSuccess();

        mockClient.Verify(c => c.Track(
            It.IsAny<string>(),
            context,
            It.Is<LdValue>(v => v.Get("graphKey").IsNull),
            It.IsAny<double>()), Times.Once);
    }

    // Test 3a: ResumptionToken includes graphKey when set; round-trips via FromResumptionToken
    [Fact]
    public void ResumptionTokenIncludesGraphKeyWhenSet()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        const string graphKey = "my-graph";
        var tracker = MakeTracker(mockClient.Object, context, graphKey);

        var token = tracker.ResumptionToken;
        Assert.NotEmpty(token);

        // Decode and verify graphKey is present
        var base64 = token.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var doc = JsonDocument.Parse(json);
        Assert.Equal(graphKey, doc.RootElement.GetProperty("graphKey").GetString());
    }

    // Test 3b: ResumptionToken omits graphKey when not set
    [Fact]
    public void ResumptionTokenOmitsGraphKeyWhenNotSet()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context, graphKey: null);

        var token = tracker.ResumptionToken;

        var base64 = token.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("graphKey", out _));
    }

    // Test 3c: FromResumptionToken reconstructs tracker with same graphKey
    [Fact]
    public void FromResumptionTokenRoundTripsGraphKey()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        const string graphKey = "round-trip-graph";
        var original = MakeTracker(mockClient.Object, context, graphKey);

        var token = original.ResumptionToken;
        var reconstructed = LdAiConfigTracker.FromResumptionToken(token, mockClient.Object, context);

        // The reconstructed tracker should include graphKey in track data
        reconstructed.TrackSuccess();
        mockClient.Verify(c => c.Track(
            It.IsAny<string>(),
            context,
            It.Is<LdValue>(v => v.Get("graphKey").AsString == graphKey),
            It.IsAny<double>()), Times.Once);
    }
}
