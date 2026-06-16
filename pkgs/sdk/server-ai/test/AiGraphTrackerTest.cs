using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Ai.Graph;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// Tests for AiGraphTracker (spec tests 14–25).
/// </summary>
public class AiGraphTrackerTest
{
    private static Mock<ILaunchDarklyClient> MockClient()
    {
        var mock = new Mock<ILaunchDarklyClient>();
        mock.Setup(c => c.GetLogger()).Returns(new Mock<ILogger>().Object);
        return mock;
    }

    private static AiGraphTracker MakeTracker(ILaunchDarklyClient client, Context context,
        string graphKey = "my-graph", string variationKey = "v1", int version = 2, string runId = null)
    {
        return new AiGraphTracker(client, graphKey, version, context, variationKey, runId);
    }

    // Test 14: TrackInvocationSuccess fires correct event with track data
    [Fact]
    public void TrackInvocationSuccessFiresCorrectEvent()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackInvocationSuccess();

        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:invocation_success",
            context,
            It.Is<LdValue>(v =>
                v.Get("graphKey").AsString == "my-graph" &&
                v.Get("version").AsInt == 2 &&
                v.Get("variationKey").AsString == "v1" &&
                v.Get("runId").Type == LdValueType.String),
            1.0), Times.Once);
    }

    // Test 15: TrackInvocationFailure fires correct event
    [Fact]
    public void TrackInvocationFailureFiresCorrectEvent()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackInvocationFailure();

        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:invocation_failure",
            context,
            It.Is<LdValue>(v => v.Get("graphKey").AsString == "my-graph"),
            1.0), Times.Once);
    }

    // Test 16: TrackDuration fires correct event with duration as metric value
    [Fact]
    public void TrackDurationFiresCorrectEvent()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackDuration(250.5);

        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:duration:total",
            context,
            It.Is<LdValue>(v => v.Get("graphKey").AsString == "my-graph"),
            250.5), Times.Once);
    }

    // Test 17: TrackTotalTokens fires correct event
    [Fact]
    public void TrackTotalTokensFiresCorrectEvent()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackTotalTokens(new Usage(100, 60, 40));

        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:total_tokens",
            context,
            It.Is<LdValue>(v => v.Get("graphKey").AsString == "my-graph"),
            100.0), Times.Once);
    }

    // Test 18: TrackPath fires correct event with path array in data
    [Fact]
    public void TrackPathFiresCorrectEventWithPathInData()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackPath(new[] { "agent-a", "agent-b", "agent-c" });

        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:path",
            context,
            It.Is<LdValue>(v =>
                v.Get("graphKey").AsString == "my-graph" &&
                v.Get("path").Type == LdValueType.Array &&
                v.Get("path").Count == 3 &&
                v.Get("path").Get(0).AsString == "agent-a" &&
                v.Get("path").Get(1).AsString == "agent-b" &&
                v.Get("path").Get(2).AsString == "agent-c"),
            1.0), Times.Once);
    }

    // Test 19: At-most-once — second TrackDuration logs warning and drops
    [Fact]
    public void TrackDurationAtMostOnce()
    {
        var mockLogger = new Mock<ILogger>();
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(mockLogger.Object);
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackDuration(100.0);
        tracker.TrackDuration(200.0);

        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:duration:total",
            context,
            It.IsAny<LdValue>(),
            It.IsAny<double>()), Times.Once);
        mockLogger.Verify(l => l.Warn(It.IsAny<string>(), It.IsAny<object[]>()), Times.Once);
    }

    // Test 20: Success/failure share slot: TrackInvocationSuccess then TrackInvocationFailure → second dropped
    [Fact]
    public void InvocationSuccessAndFailureShareSlot()
    {
        var mockLogger = new Mock<ILogger>();
        var mockClient = new Mock<ILaunchDarklyClient>();
        mockClient.Setup(c => c.GetLogger()).Returns(mockLogger.Object);
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackInvocationSuccess();
        tracker.TrackInvocationFailure();

        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:invocation_success",
            context,
            It.IsAny<LdValue>(),
            It.IsAny<double>()), Times.Once);
        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:invocation_failure",
            context,
            It.IsAny<LdValue>(),
            It.IsAny<double>()), Times.Never);
        mockLogger.Verify(l => l.Warn(It.IsAny<string>(), It.IsAny<object[]>()), Times.Once);
    }

    // Test 21: Edge methods (redirect, handoff success/failure) are multi-fire
    [Fact]
    public void EdgeMethodsAreMultiFire()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackRedirect("a", "b");
        tracker.TrackRedirect("a", "c");
        tracker.TrackHandoffSuccess("a", "b");
        tracker.TrackHandoffSuccess("b", "c");
        tracker.TrackHandoffFailure("a", "x");
        tracker.TrackHandoffFailure("b", "y");

        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:redirect", context, It.IsAny<LdValue>(), It.IsAny<double>()), Times.Exactly(2));
        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:handoff_success", context, It.IsAny<LdValue>(), It.IsAny<double>()), Times.Exactly(2));
        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:handoff_failure", context, It.IsAny<LdValue>(), It.IsAny<double>()), Times.Exactly(2));
    }

    // Test 22: Summary reflects tracked values incrementally
    [Fact]
    public void SummaryReflectsTrackedValues()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        Assert.Null(tracker.Summary.Success);
        Assert.Null(tracker.Summary.DurationMs);

        tracker.TrackInvocationSuccess();
        Assert.Equal(true, tracker.Summary.Success);

        tracker.TrackDuration(100.0);
        Assert.Equal(100.0, tracker.Summary.DurationMs);

        tracker.TrackTotalTokens(new Usage(50, 30, 20));
        Assert.Equal(50, tracker.Summary.Tokens?.Total);

        tracker.TrackPath(new[] { "a", "b" });
        Assert.Equal(new[] { "a", "b" }, tracker.Summary.Path);
    }

    // Test 23: GetTrackData returns correct RunId, GraphKey, VariationKey, Version
    [Fact]
    public void GetTrackDataReturnsCorrectFields()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var runId = Guid.NewGuid().ToString();
        var tracker = new AiGraphTracker(mockClient.Object, "graph-key", 3, context, "vkey", runId);

        var td = tracker.GetTrackData();
        Assert.Equal(runId, td.RunId);
        Assert.Equal("graph-key", td.GraphKey);
        Assert.Equal("vkey", td.VariationKey);
        Assert.Equal(3, td.Version);
    }

    // Test 23b: VariationKey is null in GetTrackData when not provided
    [Fact]
    public void GetTrackDataVariationKeyNullWhenAbsent()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = new AiGraphTracker(mockClient.Object, "graph-key", 1, context);

        var td = tracker.GetTrackData();
        Assert.Null(td.VariationKey);
    }

    // Test 24: ResumptionToken round-trips correctly
    [Fact]
    public void ResumptionTokenRoundTrips()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var runId = Guid.NewGuid().ToString();
        var tracker = new AiGraphTracker(mockClient.Object, "graph-key", 5, context, "var-key", runId);

        var token = tracker.ResumptionToken;
        Assert.NotEmpty(token);

        var base64 = token.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var doc = JsonDocument.Parse(json);
        Assert.Equal(runId, doc.RootElement.GetProperty("runId").GetString());
        Assert.Equal("graph-key", doc.RootElement.GetProperty("graphKey").GetString());
        Assert.Equal("var-key", doc.RootElement.GetProperty("variationKey").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("version").GetInt32());
    }

    // Test 24b: ResumptionToken omits variationKey when absent
    [Fact]
    public void ResumptionTokenOmitsVariationKeyWhenAbsent()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = new AiGraphTracker(mockClient.Object, "graph-key", 1, context);

        var token = tracker.ResumptionToken;
        var base64 = token.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("variationKey", out _));
    }

    // Test 25: FromResumptionToken reconstructs tracker with same runId
    [Fact]
    public void FromResumptionTokenReconstructsTracker()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var runId = Guid.NewGuid().ToString();
        var original = new AiGraphTracker(mockClient.Object, "graph-key", 2, context, "vkey", runId);

        var token = original.ResumptionToken;
        var reconstructed = AiGraphTracker.FromResumptionToken(token, mockClient.Object, context);

        var td = reconstructed.GetTrackData();
        Assert.Equal(runId, td.RunId);
        Assert.Equal("graph-key", td.GraphKey);
        Assert.Equal("vkey", td.VariationKey);
        Assert.Equal(2, td.Version);
    }

    [Fact]
    public void FromResumptionTokenThrowsOnNullToken()
    {
        var mockClient = MockClient();
        Assert.Throws<ArgumentNullException>(() =>
            AiGraphTracker.FromResumptionToken(null, mockClient.Object, Context.New("u")));
    }

    [Fact]
    public void FromResumptionTokenThrowsOnMalformedToken()
    {
        var mockClient = MockClient();
        Assert.Throws<ArgumentException>(() =>
            AiGraphTracker.FromResumptionToken("not-valid-base64!!!", mockClient.Object, Context.New("u")));
    }

    [Fact]
    public void EdgeMethodsIncludeSourceAndTargetKeys()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = MakeTracker(mockClient.Object, context);

        tracker.TrackRedirect("src-node", "redir-node");
        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:redirect",
            context,
            It.Is<LdValue>(v =>
                v.Get("sourceKey").AsString == "src-node" &&
                v.Get("redirectedTarget").AsString == "redir-node"),
            1.0), Times.Once);

        tracker.TrackHandoffSuccess("src-node", "tgt-node");
        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:handoff_success",
            context,
            It.Is<LdValue>(v =>
                v.Get("sourceKey").AsString == "src-node" &&
                v.Get("targetKey").AsString == "tgt-node"),
            1.0), Times.Once);

        tracker.TrackHandoffFailure("src-node", "bad-node");
        mockClient.Verify(c => c.Track(
            "$ld:ai:graph:handoff_failure",
            context,
            It.Is<LdValue>(v =>
                v.Get("sourceKey").AsString == "src-node" &&
                v.Get("targetKey").AsString == "bad-node"),
            1.0), Times.Once);
    }

    [Fact]
    public void RunIdIsAutoGeneratedWhenNotProvided()
    {
        var mockClient = MockClient();
        var context = Context.New("user");
        var tracker = new AiGraphTracker(mockClient.Object, "graph-key", 1, context);

        var td = tracker.GetTrackData();
        Assert.NotEmpty(td.RunId);
        // Should be a valid GUID format
        Assert.True(Guid.TryParse(td.RunId, out _));
    }
}
