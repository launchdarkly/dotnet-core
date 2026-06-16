using System.Net;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using DotNetEnv;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Ai;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

Env.TraversePath().Load();

var sdkKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_SDK_KEY")
    ?? throw new InvalidOperationException("Set LAUNCHDARKLY_SDK_KEY");
var completionKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_COMPLETION_KEY")
    ?? "sample-completion";

var ldClient = new LdClient(Configuration.Builder(sdkKey).Build());
var aiClient = new LdAiClient(new LdClientAdapter(ldClient));

// Set up the evaluation context. This context should appear on your
// LaunchDarkly contexts dashboard soon after you run the demo.
var context = Context.Builder(ContextKind.Of("user"), "example-user-key")
    .Name("Sandy")
    .Build();

// The AWS SDK reads credentials and the default region from environment
// variables, the shared credentials file, or IAM role metadata.
var bedrockClient = new AmazonBedrockRuntimeClient();

try
{
    // Pass a defaultValue for improved resiliency when the AI config is
    // unavailable or LaunchDarkly is unreachable; omit for a disabled default.
    // Example:
    //   var defaultValue = LdAiCompletionConfigDefault.New()
    //       .Enable()
    //       .SetModelName("anthropic.claude-3-sonnet-20240229-v1:0")
    //       .SetModelProviderName("bedrock")
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

    var (chatMessages, systemMessages) = MapMessages(config.Messages);

    var sampleQuestion = "What can you help me with?";
    chatMessages.Add(new Message
    {
        Role = ConversationRole.User,
        Content = new List<ContentBlock> { new() { Text = sampleQuestion } }
    });

    var modelId = string.IsNullOrEmpty(config.Model.Name) ? "no-model" : config.Model.Name;

    Console.WriteLine();
    Console.WriteLine($"Sending sample question to {modelId}: \"{sampleQuestion}\"");
    Console.WriteLine("Waiting for response...");

    var response = await tracker.TrackMetricsOf(
        ExtractBedrockMetrics,
        async () => await bedrockClient.ConverseAsync(new ConverseRequest
        {
            ModelId = modelId,
            Messages = chatMessages,
            System = systemMessages,
            InferenceConfig = BuildInferenceConfig(config.Model.Parameters)
        }));

    var aiResponse = response.Output?.Message?.Content?.FirstOrDefault()?.Text ?? "";
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

static AiMetrics ExtractBedrockMetrics(ConverseResponse res) => new(
    success: res.HttpStatusCode == HttpStatusCode.OK,
    tokens: res.Usage is null
        ? null
        : new Usage(
            Total: res.Usage.TotalTokens,
            Input: res.Usage.InputTokens,
            Output: res.Usage.OutputTokens));

static (List<Message> ChatMessages, List<SystemContentBlock> SystemMessages) MapMessages(
    IReadOnlyList<LdAiConfigTypes.Message> sdkMessages)
{
    var chat = new List<Message>();
    var system = new List<SystemContentBlock>();
    foreach (var m in sdkMessages)
    {
        if (m.Role == LdAiConfigTypes.Role.System)
        {
            system.Add(new SystemContentBlock { Text = m.Content });
            continue;
        }

        chat.Add(new Message
        {
            Role = m.Role == LdAiConfigTypes.Role.Assistant
                ? ConversationRole.Assistant
                : ConversationRole.User,
            Content = new List<ContentBlock> { new() { Text = m.Content } }
        });
    }
    return (chat, system);
}

static InferenceConfiguration BuildInferenceConfig(IReadOnlyDictionary<string, LdValue> parameters)
{
    var config = new InferenceConfiguration();
    if (parameters.TryGetValue("temperature", out var temp) && !temp.IsNull)
    {
        config.Temperature = temp.AsFloat;
    }
    if (parameters.TryGetValue("maxTokens", out var maxTokens) && !maxTokens.IsNull)
    {
        config.MaxTokens = maxTokens.AsInt;
    }
    return config;
}

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
