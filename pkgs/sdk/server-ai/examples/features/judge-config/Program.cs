using System.Text.Json;
using DotNetEnv;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Ai;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Tracking;
using OpenAI.Chat;

Env.TraversePath().Load();

var sdkKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_SDK_KEY")
    ?? throw new InvalidOperationException("Set LAUNCHDARKLY_SDK_KEY");
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY");
var judgeKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_JUDGE_KEY")
    ?? "sample-judge";

var ldClient = new LdClient(Configuration.Builder(sdkKey).Build());
var aiClient = new LdAiClient(new LdClientAdapter(ldClient));

var context = Context.Builder(ContextKind.Of("user"), "example-user-key")
    .Name("Sandy")
    .Build();

// Default judge config used as a fallback when LaunchDarkly is unreachable.
// The judge prompt instructs the model to reply with a JSON object so the
// caller can extract a numeric score.
var defaultValue = LdAiJudgeConfigDefault.New()
    .Enable()
    .SetModelName("gpt-4")
    .SetModelProviderName("openai")
    .SetEvaluationMetricKey("quality")
    .AddMessage(
        "You are an evaluator. Score the assistant response from 0.0 (poor) to " +
        "1.0 (excellent) based on quality and relevance. " +
        "Respond with JSON of the form {\"score\": <number>, \"reasoning\": \"<string>\"}.",
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

    Console.WriteLine($"Resolved judge model:        {judgeConfig.Model.Name}");
    Console.WriteLine($"Evaluation metric key:       {judgeConfig.EvaluationMetricKey}");

    var tracker = judgeConfig.CreateTracker();
    var modelName = string.IsNullOrEmpty(judgeConfig.Model.Name) ? "gpt-4" : judgeConfig.Model.Name;
    var chatClient = new ChatClient(modelName, openAiKey);

    var messages = judgeConfig.Messages.Select(ToOpenAiMessage).ToList();

    Console.WriteLine();
    Console.WriteLine($"Asking {modelName} to evaluate the response...");

    var completion = await tracker.TrackMetricsOf(
        result => new AiMetrics(
            success: true,
            tokens: new Usage(
                Total: result.Value.Usage.TotalTokenCount,
                Input: result.Value.Usage.InputTokenCount,
                Output: result.Value.Usage.OutputTokenCount)),
        async () => await chatClient.CompleteChatAsync(messages));

    var judgeResponse = completion.Value.Content[0].Text;
    Console.WriteLine();
    Console.WriteLine("Judge raw response:");
    Console.WriteLine(judgeResponse);

    var (score, reasoning, parsed) = ParseJudgeResponse(judgeResponse);

    // Emit the judge result back to LaunchDarkly. The event is silently
    // dropped when Sampled or Success is false.
    tracker.TrackJudgeResult(new JudgeResult(
        metricKey: judgeConfig.EvaluationMetricKey,
        score: score,
        sampled: true,
        success: parsed,
        judgeConfigKey: judgeKey));

    Console.WriteLine();
    Console.WriteLine($"Parsed score:                {score}");
    if (!string.IsNullOrEmpty(reasoning))
    {
        Console.WriteLine($"Reasoning:                   {reasoning}");
    }

    PrintSummary(tracker.Summary);
}
catch (Exception ex)
{
    // In production, sanitize before logging — provider errors may include credentials.
    Console.Error.WriteLine($"Error: {ex.Message}");
}
finally
{
    ldClient.FlushAndWait(TimeSpan.FromSeconds(5));
    ldClient.Dispose();
}

static ChatMessage ToOpenAiMessage(LdAiConfigTypes.Message m) => m.Role switch
{
    LdAiConfigTypes.Role.System => new SystemChatMessage(m.Content),
    LdAiConfigTypes.Role.Assistant => new AssistantChatMessage(m.Content),
    LdAiConfigTypes.Role.User => new UserChatMessage(m.Content),
    _ => new UserChatMessage(m.Content)
};

static (double Score, string Reasoning, bool Parsed) ParseJudgeResponse(string text)
{
    try
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var score = root.TryGetProperty("score", out var scoreEl) ? scoreEl.GetDouble() : 0.0;
        var reasoning = root.TryGetProperty("reasoning", out var reasoningEl)
            ? reasoningEl.GetString() ?? ""
            : "";
        return (score, reasoning, true);
    }
    catch (JsonException)
    {
        return (0.0, "", false);
    }
}

static void PrintSummary(MetricSummary summary)
{
    Console.WriteLine();
    Console.WriteLine("Done! The judge invocation was tracked with the following metrics:");
    Console.WriteLine($"  Duration:      {summary.DurationMs}ms");
    Console.WriteLine($"  Success:       {summary.Success}");
    if (summary.Tokens is { } tokens)
    {
        Console.WriteLine($"  Input tokens:  {tokens.Input}");
        Console.WriteLine($"  Output tokens: {tokens.Output}");
        Console.WriteLine($"  Total tokens:  {tokens.Total}");
    }
}
