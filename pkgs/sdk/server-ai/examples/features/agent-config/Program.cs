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

// Set up the evaluation context. This context should appear on your
// LaunchDarkly contexts dashboard soon after you run the demo.
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

    Console.WriteLine();
    Console.WriteLine($"Resolved agent model: {agentConfig.Model.Name} (provider: {agentConfig.Provider.Name})");
    Console.WriteLine();
    Console.WriteLine("Agent instructions (Mustache variables interpolated):");
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

    // To keep this example provider-agnostic, the operation below is a
    // synthetic stand-in for an actual model call — see the getting-started/*
    // examples for end-to-end integrations with OpenAI or AWS Bedrock. The
    // tracker API is exercised exactly the same way regardless of provider.
    Console.WriteLine();
    Console.WriteLine("Running a synthetic agent invocation wrapped in TrackMetricsOf...");

    var result = await tracker.TrackMetricsOf(
        SyntheticMetrics,
        async () =>
        {
            await Task.Delay(150);
            return new SyntheticResult(
                Content: "[simulated agent response]",
                InputTokens: 80,
                OutputTokens: 120,
                ToolsInvoked: new[] { "search", "calculator" });
        });

    // Record each tool invocation on the tracker so it appears in the
    // metric summary. In a real integration these names would come from
    // the provider's tool-use response.
    foreach (var toolName in result.ToolsInvoked)
    {
        tracker.TrackToolCall(toolName);
    }

    Console.WriteLine();
    Console.WriteLine("Simulated agent response:");
    Console.WriteLine(result.Content);

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

static AiMetrics SyntheticMetrics(SyntheticResult result) => new(
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

internal sealed record SyntheticResult(
    string Content,
    int InputTokens,
    int OutputTokens,
    IReadOnlyList<string> ToolsInvoked);
