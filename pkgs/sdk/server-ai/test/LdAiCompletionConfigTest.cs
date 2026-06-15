using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Evals;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;
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
    public void CreateTrackerIsNonNullWhenServerReturnsNullJson()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        // Returning LdValue.Null forces the tolerant parse to fall through to typed defaults
        // for every field. The returned config must still produce a working tracker.
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
    public void CreateTrackerIsNonNullWhenMessageTemplateIsMalformed()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();

        // A malformed Mustache template causes the template Compile step to throw inside the
        // message loop. The tolerant parse falls back to raw content for that one message and
        // continues producing the rest of the config. The returned config must still produce
        // a working tracker.
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

    [Fact]
    public void Evaluator_IsNoop_WhenNoRunnerFactory()
    {
        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>())).Returns(LdValue.Null);
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);

        // No runnerFactory supplied → Evaluator should be Noop
        var client = new LdAiClient(mockClient.Object);
        var result = client.CompletionConfig("foo", Context.New("key"));

        Assert.NotNull(result.Evaluator);
        // Noop evaluator returns empty list immediately
        var evalResults = result.Evaluator.EvaluateAsync("input", "output").GetAwaiter().GetResult();
        Assert.Empty(evalResults);
    }

    [Fact]
    public async Task Evaluator_IsReal_WhenRunnerFactoryProvided()
    {
        const string judgeKey = "my-judge";
        const string judgeMetricKey = "$ld:ai:judge:test";
        const string completionJson = """
                                     {
                                         "_ldMeta": {"variationKey": "v1", "enabled": true, "mode": "completion"},
                                         "model": {},
                                         "judgeConfiguration": {
                                             "judges": [
                                                 {"key": "my-judge", "samplingRate": 1.0}
                                             ]
                                         }
                                     }
                                     """;
        const string judgeJson = """
                                 {
                                     "_ldMeta": {"variationKey": "j1", "enabled": true, "mode": "judge"},
                                     "model": {"name": "judge-model"},
                                     "provider": {"name": "openai"},
                                     "evaluationMetricKey": "$ld:ai:judge:test"
                                 }
                                 """;

        var mockClient = new Mock<ILaunchDarklyClient>();
        var mockLogger = new Mock<ILogger>();
        mockClient.Setup(x => x.GetLogger()).Returns(mockLogger.Object);
        mockClient.Setup(x =>
            x.JsonVariation("foo", It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(completionJson));
        mockClient.Setup(x =>
            x.JsonVariation(judgeKey, It.IsAny<Context>(), It.IsAny<LdValue>()))
            .Returns(LdValue.Parse(judgeJson));

        // Runner factory returns a runner that always succeeds with score 0.9
        var mockTracker = new Mock<ILdAiConfigTracker>();
        var runnerResult = new RunnerResult("ok", new AiMetrics(true),
            Parsed: new Dictionary<string, object> { ["score"] = 0.9 });
        mockTracker
            .Setup(x => x.TrackMetricsOf(
                It.IsAny<System.Func<RunnerResult, AiMetrics>>(),
                It.IsAny<System.Func<Task<RunnerResult>>>()))
            .Returns<System.Func<RunnerResult, AiMetrics>, System.Func<Task<RunnerResult>>>(
                (_, op) => op());

        var mockRunner = new Mock<IRunner>();
        mockRunner.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()))
            .ReturnsAsync(runnerResult);

        var client = new LdAiClient(mockClient.Object,
            runnerFactory: judgeConfig =>
            {
                // Tracker factory on the judge config must be wired up from the client
                return mockRunner.Object;
            });

        // Override the tracker on the judge config by hooking the mockClient
        mockClient.Setup(x =>
            x.Track(It.IsAny<string>(), It.IsAny<Context>(), It.IsAny<LdValue>(), It.IsAny<float>()));

        var completionConfig = client.CompletionConfig("foo", Context.New("user"));

        Assert.NotNull(completionConfig.Evaluator);

        // The evaluator is not Noop — it should actually use the runner
        // We verify this by checking the runner gets called during evaluation
        // (we can't easily distinguish Noop vs real without running it, so run it)
        var evalResults = await completionConfig.Evaluator.EvaluateAsync("user input", "model output");

        Assert.Single(evalResults);
        Assert.Equal(judgeMetricKey, evalResults[0].MetricKey);
        Assert.Equal(0.9, evalResults[0].Score);
        Assert.True(evalResults[0].Success);
        mockRunner.Verify(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>()), Times.Once);
    }
}
