# Agent Config

This example focuses on the LaunchDarkly AI SDK itself — it is **provider-agnostic** and does not call OpenAI, Bedrock, Anthropic, or any other model. The actual agent invocation is replaced with a synthetic operation so you can see the SDK's tracking surface clearly. For end-to-end provider integrations see the [getting-started/](../../getting-started) examples.

It demonstrates:

- Retrieving agent instructions and tools from LaunchDarkly via `LdAiClient.AgentConfig`
- Using the builder pattern (`LdAiAgentConfigDefault.New()`) to declare a fallback default
- Interpolating Mustache placeholders in agent instructions
- Enumerating the tools attached to the agent (so they can be mapped to your provider's function-calling API)
- Tracking metrics via `tracker.TrackMetricsOf` and recording individual tool invocations via `tracker.TrackToolCall`

## Prerequisites

- .NET 8.0 SDK
- A LaunchDarkly account and a server-side SDK key

## Setup

1. [Create an AI Config](https://launchdarkly.com/docs/home/ai-configs/create) of type **Agent** in LaunchDarkly with the key `sample-agent`. Set the instructions (you may include Mustache placeholders such as `{{companyName}}`), choose a provider and model.
2. Copy `.env.example` to `.env` and fill in your SDK key:
   ```sh
   cp .env.example .env
   ```
3. Restore dependencies:
   ```sh
   dotnet restore
   ```

## Run

```sh
dotnet run
```
