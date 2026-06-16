using DotNetEnv;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Ai;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

Env.TraversePath().Load();

var sdkKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_SDK_KEY");
if (string.IsNullOrEmpty(sdkKey))
{
    Console.Error.WriteLine(
        "LaunchDarkly SDK key is required: set the LAUNCHDARKLY_SDK_KEY environment variable and try again.");
    return;
}

// Set judgeKey to the AI config key you want to evaluate.
var judgeKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_JUDGE_KEY")
    ?? "sample-judge";

var ldClient = new LdClient(Configuration.Builder(sdkKey).Build());
if (!ldClient.Initialized)
{
    Console.Error.WriteLine(
        "*** SDK failed to initialize. Please check your internet connection and SDK credential for any typo.");
    ldClient.Dispose();
    return;
}
Console.WriteLine("*** SDK successfully initialized!");

var aiClient = new LdAiClient(new LdClientAdapter(ldClient));

// Set up the evaluation context. This context should appear on your
// LaunchDarkly contexts dashboard soon after you run the demo.
var context = Context.Builder(ContextKind.Of("user"), "example-user-key")
    .Name("Sandy")
    .Build();

// Default judge config used as a fallback when LaunchDarkly is unreachable.
var defaultValue = LdAiJudgeConfigDefault.New()
    .Enable()
    .SetModelName("gpt-4")
    .SetModelProviderName("openai")
    .SetEvaluationMetricKey("quality")
    .AddMessage(
        "You are an evaluator. Score the assistant response from 0.0 (poor) to " +
        "1.0 (excellent) based on quality and relevance.",
        LdAiConfigTypes.Role.System)
    .AddMessage("Input given to the assistant: {{input}}", LdAiConfigTypes.Role.User)
    .AddMessage("Response to evaluate: {{response_to_evaluate}}", LdAiConfigTypes.Role.User)
    .Build();

// Sample input/output to evaluate. In a real application these would come
// from a previous completion call (typically a tracker resumed via
// LdAiClient.CreateTracker with a resumption token).
const string inputText = "How can you help me?";
const string outputText = "I can answer any question except questions about LaunchDarkly.";

try
{
    var judgeConfig = aiClient.JudgeConfig(
        judgeKey,
        context,
        defaultValue,
        variables: new Dictionary<string, object>
        {
            ["input"] = inputText,
            ["response_to_evaluate"] = outputText
        });

    if (!judgeConfig.Enabled)
    {
        Console.WriteLine($"Judge config '{judgeKey}' is disabled.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"Resolved judge model:    {judgeConfig.Model.Name} (provider: {judgeConfig.Provider.Name})");
    Console.WriteLine($"Evaluation metric key:   {judgeConfig.EvaluationMetricKey}");

    if (judgeConfig.Messages.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Resolved judge prompt (Mustache variables interpolated):");
        foreach (var m in judgeConfig.Messages)
        {
            Console.WriteLine($"  [{m.Role}] {m.Content}");
        }
    }

    var tracker = judgeConfig.CreateTracker();

    // To keep this example provider-agnostic, we wrap a synthetic evaluation in
    // TrackMetricsOf and report a hardcoded score. In a real integration the
    // operation would call your model provider with the judge's resolved
    // messages and parse the score from the response — see the
    // getting-started/* examples for end-to-end provider integrations.
    Console.WriteLine();
    Console.WriteLine("Running a synthetic judge evaluation wrapped in TrackMetricsOf...");

    var evaluation = await tracker.TrackMetricsOf(
        SyntheticMetrics,
        async () =>
        {
            await Task.Delay(150);
            return new SyntheticJudgeResult(
                Score: 0.8,
                Reasoning: "Response was clear but explicitly refused to help with the requested topic.",
                InputTokens: 120,
                OutputTokens: 60);
        });

    // Emit the judge result back to LaunchDarkly. The event is silently
    // dropped when Sampled or Success is false.
    tracker.TrackJudgeResult(new JudgeResult(
        metricKey: judgeConfig.EvaluationMetricKey,
        score: evaluation.Score,
        sampled: true,
        success: true,
        judgeConfigKey: judgeKey));

    Console.WriteLine();
    Console.WriteLine($"Simulated score:        {evaluation.Score}");
    Console.WriteLine($"Reasoning:              {evaluation.Reasoning}");

    PrintSummary(tracker.Summary);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
}
finally
{
    ldClient.FlushAndWait(TimeSpan.FromSeconds(5));
    ldClient.Dispose();
}

static AiMetrics SyntheticMetrics(SyntheticJudgeResult result) => new(
    success: true,
    tokens: new Usage(
        Total: result.InputTokens + result.OutputTokens,
        Input: result.InputTokens,
        Output: result.OutputTokens));

static void PrintSummary(MetricSummary summary)
{
    Console.WriteLine();
    Console.WriteLine("Done! The tracker captured the following metrics:");
    Console.WriteLine($"  Duration:      {summary.DurationMs}ms");
    Console.WriteLine($"  Success:       {summary.Success}");
    if (summary.Tokens is { } tokens)
    {
        Console.WriteLine($"  Input tokens:  {tokens.Input}");
        Console.WriteLine($"  Output tokens: {tokens.Output}");
        Console.WriteLine($"  Total tokens:  {tokens.Total}");
    }
}

internal sealed record SyntheticJudgeResult(
    double Score,
    string Reasoning,
    int InputTokens,
    int OutputTokens);
