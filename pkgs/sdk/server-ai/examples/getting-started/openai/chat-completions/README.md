# OpenAI Chat Completions Example

This example demonstrates how to use the LaunchDarkly AI SDK for .NET with the [OpenAI Chat Completions API](https://platform.openai.com/docs/api-reference/chat).

## Prerequisites

- .NET 8.0 SDK
- A LaunchDarkly account and a server-side SDK key
- An [OpenAI API key](https://platform.openai.com/api-keys)

## Setup

1. [Create an AI Config](https://launchdarkly.com/docs/home/ai-configs/create) in LaunchDarkly with the key `sample-completion`. Select OpenAI as the provider, a model such as `gpt-4`, and add a system message.
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
