# Completion Config with Default Values

This example demonstrates advanced features of `LdAiClient.CompletionConfig`:

- Building a fallback default config with `LdAiCompletionConfigDefault.New()`
- Using Mustache template variables to interpolate prompt content
- Extracting typed model parameters (`temperature`, `maxTokens`) from the config
- Enumerating tools configured on the AI Config
- Calling OpenAI with the extracted parameters and tracking metrics via `TrackMetricsOf`

## Prerequisites

- .NET 8.0 SDK
- A LaunchDarkly account and a server-side SDK key
- An [OpenAI API key](https://platform.openai.com/api-keys)

## Setup

1. [Create an AI Config](https://launchdarkly.com/docs/home/ai-configs/create) in LaunchDarkly with the key `sample-completion`. Add a system message that contains a Mustache variable (for example `You are a helpful assistant for {{companyName}}.`). Optionally set `temperature` and `maxTokens` model parameters.
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
