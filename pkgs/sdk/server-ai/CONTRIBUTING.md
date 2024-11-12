# Contributing to the LaunchDarkly AI SDK (server-side) for .NET

LaunchDarkly has published an [SDK contributor's guide](https://docs.launchdarkly.com/sdk/concepts/contributors-guide) that provides a detailed explanation of how our SDKs work. See below for additional information on how to contribute to this SDK.

## Submitting bug reports and feature requests

The LaunchDarkly SDK team monitors the [issue tracker](https://github.com/launchdarkly/dotnet-core/issues) in the SDK repository. Bug reports and feature requests specific to this SDK should be filed in this issue tracker. The SDK team will respond to all newly filed issues within two business days.

## Submitting pull requests

We encourage pull requests and other contributions from the community. Before submitting pull requests, ensure that all temporary or unintended code is removed. Don't worry about adding reviewers to the pull request; the LaunchDarkly SDK team will add themselves. The SDK team will acknowledge all pull requests within two business days.

## Build instructions

### Prerequisites

To set up your SDK build time environment, you must [download .NET development tools and follow the instructions](https://dotnet.microsoft.com/download). 

The AI SDK wraps the [Server-Side](../server/index.md) SDK.

### Building

To install all required packages:

```bash
dotnet restore
```

Then, to build the SDK for all target frameworks:

```bash
dotnet build src/LaunchDarkly.ServerSdk.Ai
```

Or, in Linux:

```bash
make
```

Or, to build for only one target framework (in this example, .NET Standard 2.0):

```bash
dotnet build src/LaunchDarkly.ServerSdk.Ai -f netstandard2.0
```

### Testing

To run all unit tests:

```bash
dotnet test test/LaunchDarkly.ServerSdk.Ai.Tests/LaunchDarkly.ServerSdk.Ai.Tests.csproj
```

Or, in Linux:

```bash
make test
```

Note that the unit tests can only be run in Debug configuration. There is an `InternalsVisibleTo` directive that allows the test code to access internal members of the library, and assembly strong-naming in the Release configuration interferes with this.

## Documentation in code

All public types, methods, and properties should have documentation comments in the standard C# XML comment format. These will be automatically included in the documentation that is generated on release; this process also uses additional Markdown content from the respective packages `docs-src/` subdirectory.

See [`docs-src/README.md`](./docs-src/README.md) for more details.
