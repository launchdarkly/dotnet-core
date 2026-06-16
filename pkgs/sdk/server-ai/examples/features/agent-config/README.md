# Agent Config

This example demonstrates how to retrieve an AI Agent Config and use it with OpenAI's Chat Completions API.

It shows how to:

- Retrieve agent instructions and tools from LaunchDarkly via `LdAiClient.AgentConfig`
- Use the builder pattern (`LdAiAgentConfigDefault.New()`) to declare a fallback default
- Feed agent instructions into OpenAI as a system message
- Enumerate the tools attached to the agent (so they can be mapped to your provider's function-calling API)
- Track metrics and tool invocations via `tracker.TrackMetricsOf` and `tracker.TrackToolCall`

## Prerequisites

- .NET 8.0 SDK
- A LaunchDarkly account and a server-side SDK key
- An [OpenAI API key](https://platform.openai.com/api-keys)

## Setup

1. [Create an AI Config](https://launchdarkly.com/docs/home/ai-configs/create) of type **Agent** in LaunchDarkly with the key `sample-agent`. Set the instructions (you may include Mustache placeholders such as `{{companyName}}`), select OpenAI as the provider, and choose a model.
2. Copy `.env.example` to `.env` and fill in your keys:
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
