# Contributing to the LaunchDarkly SDK Consul Integration

LaunchDarkly has published an [SDK contributor's guide](https://docs.launchdarkly.com/sdk/concepts/contributors-guide) that provides a detailed explanation of how our SDKs work. See below for additional information on how to contribute to this project.

## Submitting bug reports and feature requests

The LaunchDarkly SDK team monitors the [issue tracker](https://github.com/launchdarkly/dotnet-server-sdk-consul/issues) in this repository. Bug reports and feature requests specific to this package should be filed in this issue tracker. The SDK team will respond to all newly filed issues within two business days.
 
## Submitting pull requests
 
We encourage pull requests and other contributions from the community. Before submitting pull requests, ensure that all temporary or unintended code is removed. Don't worry about adding reviewers to the pull request; the LaunchDarkly SDK team will add themselves. The SDK team will acknowledge all pull requests within two business days.
 
## Build instructions
 
### Prerequisites

To set up your SDK build time environment, you must [download .NET development tools and follow the instructions](https://dotnet.microsoft.com/download). .NET 6.0 is preferred, since the .NET 6.0 tools are able to build for all supported target platforms.

The project has a package dependency on `Consul`. The dependency version is intended to be the _minimum_ compatible version; applications are expected to override this with their own dependency on some higher version.

The unit test project uses code from the `dotnet-server-sdk-shared-tests` repository which is imported as a subtree. See the `README.md` file in that directory for more information.

### Building
 
To install all required packages:

```
dotnet restore
```

To build all targets of the project without running any tests:

```
dotnet build src/LaunchDarkly.ServerSdk.Consul
```

Or, to build only one target (in this case .NET Standard 2.0):

```
dotnet build src/LaunchDarkly.ServerSdk.Consul -f netstandard2.0
```

Building the code locally in the default Debug configuration does not sign the assembly and does not require a key file.

### Testing
 
To run all unit tests, for all targets (this includes .NET Framework, so you can only do this in Windows):

```
dotnet test test/LaunchDarkly.ServerSdk.Consul.Tests
```

Or, to run tests only for one target (in this case .NET Core 3.1):

```
dotnet test test/LaunchDarkly.ServerSdk.Consul.Tests -f netcoreapp3.1
```

The tests expect you to have Consul running locally on the default port, 8500. One way to do this is with Docker:

```
docker run -p 8500:8500 consul
```
