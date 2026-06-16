# Completion Config with Default Values

This example focuses on the LaunchDarkly AI SDK itself — it is **provider-agnostic** and does not call OpenAI, Bedrock, Anthropic, or any other model. The actual model call is replaced with a synthetic operation so you can see the SDK's tracking surface clearly. For end-to-end provider integrations see the [getting-started/](../../getting-started) examples.

It demonstrates:

- Building a fallback default config with `LdAiCompletionConfigDefault.New()`
- Using Mustache template variables to interpolate prompt content
- Extracting typed model parameters (`temperature`, `maxTokens`) from the resolved config
- Enumerating tools configured on the AI Config
- Wrapping any provider call in `tracker.TrackMetricsOf` and reading the resulting `MetricSummary`

## Prerequisites

- .NET 8.0 SDK
- A LaunchDarkly account and a server-side SDK key

## Setup

1. [Create an AI Config](https://launchdarkly.com/docs/home/ai-configs/create) in LaunchDarkly with the key `sample-completion`. Add a system message that contains a Mustache variable (for example `You are a helpful assistant for {{companyName}}.`). Optionally set `temperature` and `maxTokens` model parameters.
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
