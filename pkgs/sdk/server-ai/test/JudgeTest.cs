using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Evals;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class JudgeTest
{
    private static LdAiJudgeConfig MakeJudgeConfig(
        Mock<ILdAiConfigTracker> mockTracker,
        string key = "my-judge",
        string metricKey = "$ld:ai:judge:test")
    {
        return new LdAiJudgeConfig(
            key,
            enabled: true,
            variationKey: "v1",
            version: 1,
            messages: new List<LdAiConfigTypes.Message>(),
            evaluationMetricKey: metricKey,
            model: null,
            provider: null,
            trackerFactory: _ => mockTracker.Object);
    }

    private static void SetupTrackMetricsOf(Mock<ILdAiConfigTracker> mockTracker, RunnerResult toReturn)
    {
        mockTracker
            .Setup(x => x.TrackMetricsOf(
                It.IsAny<Func<RunnerResult, AiMetrics>>(),
                It.IsAny<Func<Task<RunnerResult>>>()))
            .Returns<Func<RunnerResult, AiMetrics>, Func<Task<RunnerResult>>>(
                (_, op) => op());
    }

    private static Mock<IRunner> MockRunner(RunnerResult result)
    {
        var mock = new Mock<IRunner>();
        mock.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task EvaluateAsync_SuccessfulEvaluation_ReturnsScoreAndReasoning()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var config = MakeJudgeConfig(mockTracker);
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object>
            {
                ["score"] = 0.8,
                ["reasoning"] = "Very relevant response"
            });
        SetupTrackMetricsOf(mockTracker, runnerResult);
        var mockLogger = new Mock<ILogger>();
        var judge = new Judge(config, MockRunner(runnerResult).Object, mockLogger.Object);

        var result = await judge.EvaluateAsync("user input", "model output");

        Assert.Equal("$ld:ai:judge:test", result.MetricKey);
        Assert.Equal(0.8, result.Score);
        Assert.True(result.Sampled);
        Assert.True(result.Success);
        Assert.Equal("my-judge", result.JudgeConfigKey);
        Assert.Equal("Very relevant response", result.Reasoning);
        Assert.Null(result.ErrorMessage);
        mockLogger.Verify(x => x.Warn(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_SamplingRateExceeded_ReturnsSampledFalse()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var config = MakeJudgeConfig(mockTracker);
        var mockRunner = new Mock<IRunner>();
        var judge = new Judge(config, mockRunner.Object);

        // samplingRate = 0 means no evaluations ever pass
        var result = await judge.EvaluateAsync("input", "output", samplingRate: 0.0);

        Assert.False(result.Sampled);
        Assert.False(result.Success);
        Assert.Equal(0.0, result.Score);
        // Runner must not be called when sampling rejects
        mockRunner.Verify(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()), Times.Never);
        mockTracker.Verify(x => x.TrackMetricsOf(
            It.IsAny<Func<RunnerResult, AiMetrics>>(),
            It.IsAny<Func<Task<RunnerResult>>>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_SamplingRateOne_AlwaysEvaluates()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.5 });
        SetupTrackMetricsOf(mockTracker, runnerResult);
        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, MockRunner(runnerResult).Object);

        // samplingRate = 1.0: random double is always < 1.0, so always runs
        var result = await judge.EvaluateAsync("input", "output", samplingRate: 1.0);

        Assert.True(result.Sampled);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task EvaluateAsync_FormatsInputCorrectly()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        string capturedInput = null;
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.5 });
        mockTracker
            .Setup(x => x.TrackMetricsOf(
                It.IsAny<Func<RunnerResult, AiMetrics>>(),
                It.IsAny<Func<Task<RunnerResult>>>()))
            .Returns<Func<RunnerResult, AiMetrics>, Func<Task<RunnerResult>>>(
                (_, op) => op());

        var mockRunner = new Mock<IRunner>();
        mockRunner
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
            .Callback<string, IReadOnlyDictionary<string, object>>((input, _) => capturedInput = input)
            .ReturnsAsync(runnerResult);

        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, mockRunner.Object);

        await judge.EvaluateAsync("the user message", "the model response");

        Assert.Equal(
            "MESSAGE HISTORY:\nthe user message\n\nRESPONSE TO EVALUATE:\nthe model response",
            capturedInput);
    }

    [Fact]
    public async Task EvaluateAsync_RunnerThrows_ReturnsSuccessFalseWithErrorMessage()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        mockTracker
            .Setup(x => x.TrackMetricsOf(
                It.IsAny<Func<RunnerResult, AiMetrics>>(),
                It.IsAny<Func<Task<RunnerResult>>>()))
            .Returns<Func<RunnerResult, AiMetrics>, Func<Task<RunnerResult>>>(
                (_, op) => op());

        var mockRunner = new Mock<IRunner>();
        mockRunner
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
            .ThrowsAsync(new InvalidOperationException("provider unavailable"));

        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, mockRunner.Object);

        var result = await judge.EvaluateAsync("input", "output");

        Assert.False(result.Success);
        Assert.True(result.Sampled);
        Assert.Equal(0.0, result.Score);
        Assert.Equal("provider unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task EvaluateAsync_ScoreOutOfRange_LogsWarningAndReturnsSuccessFalse()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 1.5 });
        SetupTrackMetricsOf(mockTracker, runnerResult);
        var mockLogger = new Mock<ILogger>();
        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, MockRunner(runnerResult).Object, mockLogger.Object);

        var result = await judge.EvaluateAsync("input", "output");

        Assert.False(result.Success);
        Assert.Equal(0.0, result.Score);
        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s => s.Contains("outside valid range")),
            It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_NegativeScore_LogsWarningAndReturnsSuccessFalse()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = -0.1 });
        SetupTrackMetricsOf(mockTracker, runnerResult);
        var mockLogger = new Mock<ILogger>();
        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, MockRunner(runnerResult).Object, mockLogger.Object);

        var result = await judge.EvaluateAsync("input", "output");

        Assert.False(result.Success);
        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s => s.Contains("outside valid range")),
            It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_NonStringReasoning_LogsWarningAndSetsNull()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object>
            {
                ["score"] = 0.7,
                ["reasoning"] = 42 // not a string
            });
        SetupTrackMetricsOf(mockTracker, runnerResult);
        var mockLogger = new Mock<ILogger>();
        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, MockRunner(runnerResult).Object, mockLogger.Object);

        var result = await judge.EvaluateAsync("input", "output");

        Assert.True(result.Success);
        Assert.Equal(0.7, result.Score);
        Assert.Null(result.Reasoning);
        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s => s.Contains("not a string")),
            It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotCallTrackJudgeResult()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.5 });
        SetupTrackMetricsOf(mockTracker, runnerResult);
        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, MockRunner(runnerResult).Object);

        await judge.EvaluateAsync("input", "output");

        mockTracker.Verify(x => x.TrackJudgeResult(It.IsAny<JudgeResult>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_CreatesPerEvaluationTracker()
    {
        int trackerCreateCount = 0;
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.5 });
        SetupTrackMetricsOf(mockTracker, runnerResult);

        var config = new LdAiJudgeConfig(
            "my-judge",
            enabled: true,
            variationKey: "v1",
            version: 1,
            messages: new List<LdAiConfigTypes.Message>(),
            evaluationMetricKey: "metric",
            model: null,
            provider: null,
            trackerFactory: _ =>
            {
                trackerCreateCount++;
                return mockTracker.Object;
            });

        var judge = new Judge(config, MockRunner(runnerResult).Object);
        await judge.EvaluateAsync("input1", "output1");
        await judge.EvaluateAsync("input2", "output2");

        Assert.Equal(2, trackerCreateCount);
    }

    [Fact]
    public async Task EvaluateAsync_WrapsRunnerInTrackMetricsOf()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var runnerResult = new RunnerResult(
            Content: "ok",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.5 });
        SetupTrackMetricsOf(mockTracker, runnerResult);
        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, MockRunner(runnerResult).Object);

        await judge.EvaluateAsync("input", "output");

        mockTracker.Verify(x => x.TrackMetricsOf(
            It.IsAny<Func<RunnerResult, AiMetrics>>(),
            It.IsAny<Func<Task<RunnerResult>>>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateMessagesAsync_FormatsMessagesAndDelegates()
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        string capturedInput = null;
        var runnerResult = new RunnerResult(
            Content: "the response",
            Metrics: new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.6 });
        mockTracker
            .Setup(x => x.TrackMetricsOf(
                It.IsAny<Func<RunnerResult, AiMetrics>>(),
                It.IsAny<Func<Task<RunnerResult>>>()))
            .Returns<Func<RunnerResult, AiMetrics>, Func<Task<RunnerResult>>>(
                (_, op) => op());

        var mockRunner = new Mock<IRunner>();
        mockRunner
            .Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
            .Callback<string, IReadOnlyDictionary<string, object>>((input, _) => capturedInput = input)
            .ReturnsAsync(runnerResult);

        var config = MakeJudgeConfig(mockTracker);
        var judge = new Judge(config, mockRunner.Object);
        var messages = new List<LdAiConfigTypes.Message>
        {
            new LdAiConfigTypes.Message("Hello", LdAiConfigTypes.Role.User),
            new LdAiConfigTypes.Message("Hi there", LdAiConfigTypes.Role.Assistant)
        };

        var result = await judge.EvaluateMessagesAsync(messages, runnerResult);

        Assert.True(result.Success);
        // Messages formatted as "role: content\nrole: content"
        Assert.Contains("user: Hello", capturedInput);
        Assert.Contains("assistant: Hi there", capturedInput);
        // Output is runnerResult.Content
        Assert.Contains("the response", capturedInput);
    }
}
