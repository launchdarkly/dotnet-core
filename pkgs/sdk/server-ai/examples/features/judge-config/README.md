# Judge Config

This example demonstrates how to retrieve a Judge AI Config, run it against a sample input/output pair, and report the result back to LaunchDarkly.

It shows how to:

- Retrieve a judge configuration with its prompt messages and evaluation metric key via `LdAiClient.JudgeConfig`
- Use the builder pattern (`LdAiJudgeConfigDefault.New()`) to declare a fallback default
- Pass template variables for the input under evaluation and the response to evaluate
- Call OpenAI with the interpolated judge messages
- Parse the judge's response (expected to be JSON: `{"score": <number>, "reasoning": "<string>"}`)
- Emit the judge result via `tracker.TrackJudgeResult` using the metric key from the config

## Prerequisites

- .NET 8.0 SDK
- A LaunchDarkly account and a server-side SDK key
- An [OpenAI API key](https://platform.openai.com/api-keys)

## Setup

1. [Create an AI Config](https://launchdarkly.com/docs/home/ai-configs/create) of type **Judge** in LaunchDarkly with the key `sample-judge`. Set an evaluation metric key (for example `quality`), select OpenAI as the provider, and supply judge prompts that may reference Mustache variables such as `{{input}}` and `{{response_to_evaluate}}`. The prompt should instruct the model to reply with JSON containing a numeric `score` field.
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
