# Judge Config

This example focuses on the LaunchDarkly AI SDK itself — it is **provider-agnostic** and does not call OpenAI, Bedrock, Anthropic, or any other model. The actual judge evaluation is replaced with a synthetic operation that returns a hardcoded score so you can see the SDK's tracking surface clearly. For end-to-end provider integrations see the [getting-started/](../../getting-started) examples.

It demonstrates:

- Retrieving a judge configuration (prompt messages and evaluation metric key) via `LdAiClient.JudgeConfig`
- Using the builder pattern (`LdAiJudgeConfigDefault.New()`) to declare a fallback default
- Passing template variables for the input under evaluation and the response to evaluate
- Wrapping the judge invocation in `tracker.TrackMetricsOf`
- Emitting the judge result via `tracker.TrackJudgeResult` using the metric key from the config

## Prerequisites

- .NET 8.0 SDK
- A LaunchDarkly account and a server-side SDK key

## Setup

1. [Create an AI Config](https://launchdarkly.com/docs/home/ai-configs/create) of type **Judge** in LaunchDarkly with the key `sample-judge`. Set an evaluation metric key (for example `quality`), choose a provider and model, and supply judge prompts that may reference Mustache variables such as `{{input}}` and `{{response_to_evaluate}}`.
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
