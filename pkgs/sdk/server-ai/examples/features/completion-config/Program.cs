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

var context = Context.Builder(ContextKind.Of("user"), "example-user-key")
    .Name("Sandy")
    .Build();

// Build a fallback default config using the builder pattern. The default is
// used when LaunchDarkly is unreachable or the AI Config is missing/disabled.
var defaultValue = LdAiCompletionConfigDefault.New()
    .Enable()
    .SetModelName("gpt-4")
    .SetModelProviderName("openai")
    .SetModelParam("temperature", LdValue.Of(0.7))
    .SetModelParam("maxTokens", LdValue.Of(4096))
    .AddMessage("You are a helpful assistant for {{companyName}}.", LdAiConfigTypes.Role.System)
    .Build();

try
{
    // The variables dictionary supplies values for Mustache placeholders such
    // as {{companyName}} that appear in message content.
    var config = aiClient.CompletionConfig(
        completionKey,
        context,
        defaultValue,
        variables: new Dictionary<string, object>
        {
            ["companyName"] = "LaunchDarkly"
        });

    if (!config.Enabled)
    {
        Console.WriteLine($"AI config '{completionKey}' is disabled.");
        return;
    }

    Console.WriteLine($"Resolved model: {config.Model.Name} (provider: {config.Provider.Name})");

    // Read typed model parameters set on the AI Config. Falls back to a
    // sensible default if the parameter is absent or null.
    var temperature = config.Model.Parameters.TryGetValue("temperature", out var tempVal) && !tempVal.IsNull
        ? tempVal.AsFloat
        : 0.5f;
    var maxTokens = config.Model.Parameters.TryGetValue("maxTokens", out var maxVal) && !maxVal.IsNull
        ? maxVal.AsInt
        : 4096;

    Console.WriteLine($"  temperature: {temperature}");
    Console.WriteLine($"  maxTokens:   {maxTokens}");

    // Tools configured on the AI Config are exposed as a name-keyed dictionary.
    if (config.Tools.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Available tools:");
        foreach (var (name, tool) in config.Tools)
        {
            Console.WriteLine($"  {name}: {tool.Description} (type: {tool.Type})");
        }
    }

    var tracker = config.CreateTracker();
    var modelName = string.IsNullOrEmpty(config.Model.Name) ? "gpt-4" : config.Model.Name;
    var chatClient = new ChatClient(modelName, openAiKey);

    var sampleQuestion = "How can LaunchDarkly help me?";
    var messages = config.Messages
        .Select(ToOpenAiMessage)
        .Append(new UserChatMessage(sampleQuestion))
        .ToList();

    var options = new ChatCompletionOptions
    {
        Temperature = temperature,
        MaxOutputTokenCount = maxTokens
    };

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
        async () => await chatClient.CompleteChatAsync(messages, options));

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
