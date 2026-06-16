# AWS Bedrock Converse Example

This example demonstrates how to use the LaunchDarkly AI SDK for .NET with the [AWS Bedrock Converse API](https://docs.aws.amazon.com/bedrock/latest/userguide/conversation-inference.html).

## Prerequisites

- .NET 8.0 SDK
- A LaunchDarkly account and a server-side SDK key
- An AWS account with [Bedrock model access enabled](https://docs.aws.amazon.com/bedrock/latest/userguide/model-access.html)
- AWS credentials available to the AWS SDK (environment variables, the shared credentials file, or an IAM role)

## Setup

1. [Create an AI Config](https://launchdarkly.com/docs/home/ai-configs/create) in LaunchDarkly with the key `sample-completion`. Select Bedrock as the provider, set the model name to a Bedrock model ID (for example `anthropic.claude-3-sonnet-20240229-v1:0`), and add a system message.
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
