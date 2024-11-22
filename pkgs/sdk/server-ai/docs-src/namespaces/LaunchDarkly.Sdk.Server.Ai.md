The main namespace for the LaunchDarkly AI server-side .NET SDK.

You will most often use <xref:LaunchDarkly.Sdk.Server.Ai.LdAiClient> (the AI client) as well as
the <xref:LaunchDarkly.Sdk.Context> type from <xref:LaunchDarkly.Sdk>.

To get started, follow this pattern:

```csharp
using LaunchDarkly.Sdk.Server.Ai;
using LaunchDarkly.Sdk.Server.Ai.Adapters;

// This is a standard LaunchDarkly server-side .NET client instance.
var baseClient = new LdClient(Configuration.Builder("sdk-key").Build());

// The AI client wraps the base client, providing additional AI-related functionality.
var aiClient = new LdAiClient(new LdClientAdapter(baseClient));

// Pass in the key of the AI config, a context, and a default value in case the config can't be
// retrieved from LaunchDarkly.
var myModelConfig = aiClient.Config("my-model-config", Context.New("user-key"), LdAiConfig.Disabled);
```
