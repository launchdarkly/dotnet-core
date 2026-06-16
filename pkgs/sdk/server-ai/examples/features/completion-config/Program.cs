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

// Set completionKey to the AI config key you want to evaluate.
var completionKey = Environment.GetEnvironmentVariable("LAUNCHDARKLY_COMPLETION_KEY")
    ?? "sample-completion";

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

    Console.WriteLine();
    Console.WriteLine($"Resolved model:    {config.Model.Name} (provider: {config.Provider.Name})");

    // Read typed model parameters set on the AI Config. Falls back to a
    // sensible default if the parameter is absent or null.
    var temperature = config.Model.Parameters.TryGetValue("temperature", out var tempVal) && !tempVal.IsNull
        ? tempVal.AsFloat
        : 0.5f;
    var maxTokens = config.Model.Parameters.TryGetValue("maxTokens", out var maxVal) && !maxVal.IsNull
        ? maxVal.AsInt
        : 4096;

    Console.WriteLine($"  temperature:     {temperature}");
    Console.WriteLine($"  maxTokens:       {maxTokens}");

    if (config.Messages.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Resolved messages (Mustache variables interpolated):");
        foreach (var m in config.Messages)
        {
            Console.WriteLine($"  [{m.Role}] {m.Content}");
        }
    }

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

    // To keep this example provider-agnostic, the operation below is a
    // synthetic stand-in for an actual model call — see the getting-started/*
    // examples for end-to-end integrations with OpenAI or AWS Bedrock. The
    // tracker API is exercised exactly the same way regardless of provider.
    Console.WriteLine();
    Console.WriteLine("Running a synthetic model call wrapped in TrackMetricsOf...");

    var result = await tracker.TrackMetricsOf(
        SyntheticMetrics,
        async () =>
        {
            await Task.Delay(150);
            return new SyntheticResult(
                Content: "[simulated model response]",
                InputTokens: 50,
                OutputTokens: 100);
        });

    Console.WriteLine();
    Console.WriteLine("Simulated model response:");
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

internal sealed record SyntheticResult(string Content, int InputTokens, int OutputTokens);
