using DotNetEnv;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;
using LaunchDarkly.Sdk.Server.Ai;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Tracking;
using OpenAI.Chat;

Env.TraversePath().Load();

var sdkKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_SDK_KEY");
if (string.IsNullOrEmpty(sdkKey))
{
    Console.Error.WriteLine(
        "LaunchDarkly SDK key is required: set the LAUNCHDARKLY_SDK_KEY environment variable and try again.");
    return;
}

var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(openAiKey))
{
    Console.Error.WriteLine(
        "OpenAI API key is required: set the OPENAI_API_KEY environment variable and try again.");
    return;
}

// Set agentKey to the AI config key you want to evaluate.
var agentKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_AGENT_KEY")
    ?? "sample-agent";

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

var context = Context.Builder(ContextKind.Of("user"), "example-user-key")
    .Name("Sandy")
    .Build();

// Default agent config used when the AI Config is unreachable or missing.
var defaultValue = LdAiAgentConfigDefault.New()
    .Enable()
    .SetModelName("gpt-4")
    .SetModelProviderName("openai")
    .SetInstructions("You are a helpful research assistant for {{companyName}}.")
    .Build();

try
{
    var agentConfig = aiClient.AgentConfig(
        agentKey,
        context,
        defaultValue,
        variables: new Dictionary<string, object>
        {
            ["companyName"] = "LaunchDarkly"
        });

    if (!agentConfig.Enabled)
    {
        Console.WriteLine($"Agent config '{agentKey}' is disabled.");
        return;
    }

    Console.WriteLine($"Resolved agent model: {agentConfig.Model.Name} (provider: {agentConfig.Provider.Name})");
    Console.WriteLine();
    Console.WriteLine("Agent instructions:");
    Console.WriteLine(agentConfig.Instructions);

    if (agentConfig.Tools.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Configured tools (map to your provider's function-calling API):");
        foreach (var (name, tool) in agentConfig.Tools)
        {
            Console.WriteLine($"  {name}: {tool.Description} (type: {tool.Type})");
        }
    }

    var tracker = agentConfig.CreateTracker();
    var modelName = string.IsNullOrEmpty(agentConfig.Model.Name) ? "gpt-4" : agentConfig.Model.Name;
    var chatClient = new ChatClient(modelName, openAiKey);

    var sampleQuestion = "How can LaunchDarkly help me?";
    var messages = new List<ChatMessage>
    {
        new SystemChatMessage(agentConfig.Instructions),
        new UserChatMessage(sampleQuestion)
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
        async () => await chatClient.CompleteChatAsync(messages));

    // If the agent invoked tools, record each call so it shows up on the
    // agent's metric summary. Replace with the tool keys your agent actually
    // invoked at runtime.
    foreach (var toolCall in completion.Value.ToolCalls)
    {
        tracker.TrackToolCall(toolCall.FunctionName);
    }

    var aiResponse = completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : "";
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
