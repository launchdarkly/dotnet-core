# Contributing to the LaunchDarkly SDK .NET Common Code

LaunchDarkly has published an [SDK contributor's guide](https://docs.launchdarkly.com/sdk/concepts/contributors-guide) that provides a detailed explanation of how our SDKs work. See below for additional information on how to contribute to this SDK.

## Submitting bug reports and feature requests

In general, issues should be filed in the issue trackers for the [.NET server-side SDK](https://github.com/launchdarkly/dotnet-server-sdk/issues) or the [.NET client-side SDK](https://github.com/launchdarkly/dotnet-client-sdk/issues) rather than in this repository, unless you have a specific implementation issue regarding the code in this repository.

## Submitting pull requests

We encourage pull requests and other contributions from the community. Before submitting pull requests, ensure that all temporary or unintended code is removed. Don't worry about adding reviewers to the pull request; the LaunchDarkly SDK team will add themselves. The SDK team will acknowledge all pull requests within two business days.

## Build instructions

### Prerequisites

To set up your SDK build time environment, you must [download .NET Core and follow the instructions](https://dotnet.microsoft.com/download) (make sure you have 2.0 or higher).

### Building

To install all required packages:

```
dotnet restore
```

Then, to build the SDK without running any tests:

```
dotnet build src/LaunchDarkly.CommonSdk.csproj -f netstandard2.0
```

### Testing

To run all unit tests:

```
dotnet test test/LaunchDarkly.CommonSdk.Tests.csproj
```
