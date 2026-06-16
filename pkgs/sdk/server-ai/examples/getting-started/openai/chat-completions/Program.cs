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
var completionKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_COMPLETION_KEY")
    ?? "sample-completion";

var ldClient = new LdClient(Configuration.Builder(sdkKey).Build());
var aiClient = new LdAiClient(new LdClientAdapter(ldClient));

// Set up the evaluation context. This context should appear on your
// LaunchDarkly contexts dashboard soon after you run the demo.
var context = Context.Builder(ContextKind.Of("user"), "example-user-key")
    .Name("Sandy")
    .Build();

try
{
    // Pass a defaultValue for improved resiliency when the AI config is
    // unavailable or LaunchDarkly is unreachable; omit for a disabled default.
    // Example:
    //   var defaultValue = LdAiCompletionConfigDefault.New()
    //       .Enable()
    //       .SetModelName("gpt-4")
    //       .SetModelProviderName("openai")
    //       .AddMessage("You are a helpful assistant.", LdAiConfigTypes.Role.System)
    //       .Build();
    var config = aiClient.CompletionConfig(
        completionKey,
        context,
        variables: new Dictionary<string, object>
        {
            ["myUserVariable"] = "Testing Variable"
        });

    if (!config.Enabled)
    {
        Console.WriteLine(
            $"AI config '{completionKey}' is disabled. Verify the config key exists in " +
            "your LaunchDarkly project and is not targeting a disabled variation.");
        return;
    }

    var tracker = config.CreateTracker();

    var modelName = string.IsNullOrEmpty(config.Model.Name) ? "gpt-4" : config.Model.Name;
    var chatClient = new ChatClient(modelName, openAiKey);

    var sampleQuestion = "What can you help me with?";
    var messages = config.Messages
        .Select(ToOpenAiMessage)
        .Append(new UserChatMessage(sampleQuestion))
        .ToList();

    Console.WriteLine();
    Console.WriteLine($"Sending sample question to {modelName}: \"{sampleQuestion}\"");
    Console.WriteLine("Waiting for response...");

    var completion = await tracker.TrackMetricsOf(
        result => new AiMetrics(
            success: true,
            tokens: new Usage(
                Total: result.Value.Usage.TotalTokenCount,
                Input: result.Value.Usage.InputTokenCount,
                Output: result.Value.Usage.OutputTokenCount)),
        async () => await chatClient.CompleteChatAsync(messages));

    var aiResponse = completion.Value.Content[0].Text;
    Console.WriteLine();
    Console.WriteLine("Model response:");
    Console.WriteLine(aiResponse);

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

static void PrintSummary(MetricSummary summary)
{
    Console.WriteLine();
    Console.WriteLine("Done! The AI config was evaluated and the following metrics were tracked:");
    Console.WriteLine($"  Duration:      {summary.DurationMs}ms");
    Console.WriteLine($"  Success:       {summary.Success}");
    if (summary.Tokens is { } tokens)
    {
        Console.WriteLine($"  Input tokens:  {tokens.Input}");
        Console.WriteLine($"  Output tokens: {tokens.Output}");
        Console.WriteLine($"  Total tokens:  {tokens.Total}");
    }
}
