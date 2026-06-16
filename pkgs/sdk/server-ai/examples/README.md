# LaunchDarkly AI SDK for .NET - Examples

| Package | NuGet | Docs |
| --- | --- | --- |
| [LaunchDarkly.ServerSdk.Ai](https://github.com/launchdarkly/dotnet-core/tree/main/pkgs/sdk/server-ai) | [![NuGet](https://img.shields.io/nuget/v/LaunchDarkly.ServerSdk.Ai)](https://www.nuget.org/packages/LaunchDarkly.ServerSdk.Ai) | [Reference](https://docs.launchdarkly.com/sdk/ai/dotnet) |

Each example is a self-contained .NET console application you can run independently. Pick one that matches your provider or use case, follow the README, and you'll be up and running in minutes.

For more comprehensive instructions, visit the [Quickstart page](https://docs.launchdarkly.com/home/ai-configs/quickstart) or the [.NET reference guide](https://docs.launchdarkly.com/sdk/ai/dotnet).

## Getting Started

These examples show how to integrate LaunchDarkly AI with different providers.

| Provider | Example | Description |
| --- | --- | --- |
| OpenAI | [Chat Completions](getting-started/openai/chat-completions/) | `CompletionConfig` with OpenAI, automatic metrics tracking |
| Bedrock | [Converse](getting-started/bedrock/converse/) | `CompletionConfig` with the AWS Bedrock Converse API, metrics tracking |

## Features

These examples focus on the LaunchDarkly AI SDK itself. They are **provider-agnostic** — they retrieve and resolve AI configs, then exercise the tracker API with synthetic operations rather than calling any model provider. Use them to understand the SDK's surface; use the getting-started examples above to see end-to-end provider integrations.

| Example | Description |
| --- | --- |
| [Completion Config](features/completion-config/) | Default values, Mustache variables, model parameter extraction, tool enumeration, and `TrackMetricsOf` |
| [Agent Config](features/agent-config/) | Agent instructions and tools; `TrackMetricsOf` and `TrackToolCall` |
| [Judge Config](features/judge-config/) | Judge config retrieval and `TrackJudgeResult` |
