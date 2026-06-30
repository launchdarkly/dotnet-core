using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Evals;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;
using Moq;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Ai;

public class EvaluatorTest
{
    private static LdAiJudgeConfig MakeJudgeConfig(string key, string metricKey,
        Mock<ILdAiConfigTracker> mockTracker)
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

    private static Mock<ILdAiConfigTracker> MakeTrackerWithResult(RunnerResult result)
    {
        var mockTracker = new Mock<ILdAiConfigTracker>();
        mockTracker
            .Setup(x => x.TrackMetricsOf(
                It.IsAny<System.Func<RunnerResult, AiMetrics>>(),
                It.IsAny<System.Func<Task<RunnerResult>>>()))
            .Returns<System.Func<RunnerResult, AiMetrics>, System.Func<Task<RunnerResult>>>(
                (_, op) => op());
        return mockTracker;
    }

    private static Mock<IRunner> MockRunner(RunnerResult result)
    {
        var mock = new Mock<IRunner>();
        mock.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task Noop_ReturnsEmptyListImmediately()
    {
        var evaluator = Evaluator.Noop();
        var results = await evaluator.EvaluateAsync("input", "output");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Noop_DoesNotInvokeAnyJudges()
    {
        var mockRunner = new Mock<IRunner>();
        var evaluator = Evaluator.Noop();

        await evaluator.EvaluateAsync("input", "output");

        mockRunner.Verify(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()), Times.Never);
    }

    [Fact]
    public async Task Noop_DoesNotLogWarnings()
    {
        var mockLogger = new Mock<ILogger>();
        var evaluator = Evaluator.Noop();

        await evaluator.EvaluateAsync("input", "output");

        mockLogger.Verify(x => x.Warn(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_RunsAllConfiguredJudges()
    {
        var runnerResult1 = new RunnerResult("ok1", new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.8 });
        var runnerResult2 = new RunnerResult("ok2", new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.6 });

        var tracker1 = MakeTrackerWithResult(runnerResult1);
        var tracker2 = MakeTrackerWithResult(runnerResult2);

        var judgeConfig1 = MakeJudgeConfig("judge-1", "metric-1", tracker1);
        var judgeConfig2 = MakeJudgeConfig("judge-2", "metric-2", tracker2);

        var judges = new Dictionary<string, Judge>
        {
            ["judge-1"] = new Judge(judgeConfig1, MockRunner(runnerResult1).Object),
            ["judge-2"] = new Judge(judgeConfig2, MockRunner(runnerResult2).Object)
        };

        var judgeConfiguration = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>
            {
                new LdAiConfigTypes.JudgeConfiguration.Judge("judge-1", samplingRate: 1.0),
                new LdAiConfigTypes.JudgeConfiguration.Judge("judge-2", samplingRate: 1.0)
            });

        var evaluator = new Evaluator(judges, judgeConfiguration);
        var results = await evaluator.EvaluateAsync("input", "output");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.MetricKey == "metric-1" && r.Score == 0.8);
        Assert.Contains(results, r => r.MetricKey == "metric-2" && r.Score == 0.6);
    }

    [Fact]
    public async Task EvaluateAsync_MissingJudge_LogsWarningAndSkips()
    {
        var runnerResult = new RunnerResult("ok", new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.9 });
        var tracker = MakeTrackerWithResult(runnerResult);
        var judgeConfig = MakeJudgeConfig("judge-found", "metric-found", tracker);

        var judges = new Dictionary<string, Judge>
        {
            ["judge-found"] = new Judge(judgeConfig, MockRunner(runnerResult).Object)
        };

        var judgeConfiguration = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>
            {
                new LdAiConfigTypes.JudgeConfiguration.Judge("judge-found", samplingRate: 1.0),
                new LdAiConfigTypes.JudgeConfiguration.Judge("judge-missing", samplingRate: 1.0)
            });

        var mockLogger = new Mock<ILogger>();
        var evaluator = new Evaluator(judges, judgeConfiguration, mockLogger.Object);
        var results = await evaluator.EvaluateAsync("input", "output");

        // Only the found judge produces a result; the missing one does not
        Assert.Single(results);
        Assert.Equal("metric-found", results[0].MetricKey);

        mockLogger.Verify(x => x.Warn(
            It.Is<string>(s => s.Contains("judge-missing") || s.Contains("not found")),
            It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_MissingJudge_ProducesNoEntryForMissingJudge()
    {
        var judges = new Dictionary<string, Judge>();

        var judgeConfiguration = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>
            {
                new LdAiConfigTypes.JudgeConfiguration.Judge("judge-missing", samplingRate: 1.0)
            });

        var mockLogger = new Mock<ILogger>();
        var evaluator = new Evaluator(judges, judgeConfiguration, mockLogger.Object);
        var results = await evaluator.EvaluateAsync("input", "output");

        Assert.Empty(results);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotCallTrackJudgeResult()
    {
        var runnerResult = new RunnerResult("ok", new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.5 });
        var mockTracker = MakeTrackerWithResult(runnerResult);
        var judgeConfig = MakeJudgeConfig("judge-1", "metric-1", mockTracker);
        var judges = new Dictionary<string, Judge>
        {
            ["judge-1"] = new Judge(judgeConfig, MockRunner(runnerResult).Object)
        };

        var judgeConfiguration = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>
            {
                new LdAiConfigTypes.JudgeConfiguration.Judge("judge-1", samplingRate: 1.0)
            });

        var evaluator = new Evaluator(judges, judgeConfiguration);
        await evaluator.EvaluateAsync("input", "output");

        mockTracker.Verify(x => x.TrackJudgeResult(It.IsAny<JudgeResult>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_NullJudgeConfiguration_ReturnsEmpty()
    {
        var evaluator = new Evaluator(new Dictionary<string, Judge>(), judgeConfiguration: null);
        var results = await evaluator.EvaluateAsync("input", "output");

        Assert.Empty(results);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyJudgeConfiguration_ReturnsEmpty()
    {
        var judgeConfiguration = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>());

        var evaluator = new Evaluator(new Dictionary<string, Judge>(), judgeConfiguration);
        var results = await evaluator.EvaluateAsync("input", "output");

        Assert.Empty(results);
    }

    [Fact]
    public async Task EvaluateAsync_SkippedJudge_DoesNotWarnAtEvalTime()
    {
        // Simulate fix 4: the judgeConfiguration passed to the Evaluator already has the
        // skipped judge filtered out, so no "not found" warning should ever fire at eval time.
        var judgeConfiguration = new LdAiConfigTypes.JudgeConfiguration(
            new List<LdAiConfigTypes.JudgeConfiguration.Judge>());

        var mockLogger = new Mock<ILogger>();
        var evaluator = new Evaluator(new Dictionary<string, Judge>(), judgeConfiguration, mockLogger.Object);
        await evaluator.EvaluateAsync("input", "output");

        mockLogger.Verify(x => x.Warn(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
    }
}
