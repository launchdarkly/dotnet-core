# Contributing to the LaunchDarkly SDK DynamoDB Integration

The source code for this library is [here](https://github.com/launchdarkly/dotnet-core/tree/main/pkgs/dotnet-server-sdk-dynamodb). We encourage pull-requests and other contributions from the community. Since this library is meant to be used in conjunction with the LaunchDarkly .NET Server SDK, you may want to look at the [.NET Server SDK source code](https://github.com/launchdarkly/dotnet-core/tree/main/pkgs/sdk/server) and our [SDK contributor's guide](https://docs.launchdarkly.com/sdk/concepts/contributors-guide).

## Submitting bug reports and feature requests

The LaunchDarkly SDK team monitors the [issue tracker](https://github.com/launchdarkly/dotnet-core/issues) in this repository. Bug reports and feature requests specific to this package should be filed in the issue tracker. The SDK team will respond to all newly filed issues within two business days.
 
## Submitting pull requests
 
We encourage pull requests and other contributions from the community. Before submitting pull requests, ensure that all temporary or unintended code is removed. Don't worry about adding reviewers to the pull request; the LaunchDarkly SDK team will add themselves. The SDK team will acknowledge all pull requests within two business days.
 
## Build instructions
 
### Prerequisites

To set up your SDK build time environment, you must [download .NET development tools and follow the instructions](https://dotnet.microsoft.com/download). .NET 8.0 is preferred, since the .NET 8.0 tools are able to build for all supported target platforms.

The project has a package dependency on `AWSSDK.DynamoDBv2`. The dependency version is intended to be the _minimum_ compatible version; applications are expected to override this with their own dependency on some higher version.

The unit test project uses code from the `dotnet-server-sdk-shared-tests` project. See the [README.md](../shared/dotnet-server-sdk-shared-tests/README.md) file in that directory for more information.

### Building
 
To install all required packages:

```
dotnet restore
```

To build all targets of the project without running any tests:

```
dotnet build src/LaunchDarkly.ServerSdk.DynamoDB
```

Or, to build only one target (in this case .NET Standard 2.0):

```
dotnet build src/LaunchDarkly.ServerSdk.DynamoDB -f netstandard2.0
```

Building the code locally in the default Debug configuration does not sign the assembly and does not require a key file.

### Testing
 
To run all unit tests, for all targets (this includes .NET Framework, so you can only do this in Windows):

```
dotnet test test/LaunchDarkly.ServerSdk.DynamoDB.Tests
```

Or, to run tests only for one target (in this case .NET 8.0):

```
dotnet test test/LaunchDarkly.ServerSdk.DynamoDB.Tests -f net8.0
```

The tests expect you to have a local DynamoDB instance running on port 8000. One way to do this is with Docker:

```bash
docker run -p 8000:8000 amazon/dynamodb-local
```
