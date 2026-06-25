# Contributing to the LaunchDarkly SDK .NET Internal Common Code

LaunchDarkly has published an [SDK contributor's guide](https://docs.launchdarkly.com/docs/sdk-contributors-guide) that provides a detailed explanation of how our SDKs work. See below for additional information on how to contribute to this SDK.

## Submitting bug reports and feature requests

In general, issues should be filed in the issue trackers for the [.NET server-side SDK](https://github.com/launchdarkly/dotnet-server-sdk/issues) or the [Xamarin client-side SDK](https://github.com/launchdarkly/xamarin-client-sdk/issues) rather than in this repository, unless you have a specific implementation issue regarding the code in this repository.
 
## Submitting pull requests
 
We encourage pull requests and other contributions from the community. Before submitting pull requests, ensure that all temporary or unintended code is removed. Don't worry about adding reviewers to the pull request; the LaunchDarkly SDK team will add themselves. The SDK team will acknowledge all pull requests within two business days.
 
## Build instructions
 
### Prerequisites

To set up your build time environment, you must [download .NET and follow the instructions](https://dotnet.microsoft.com/download). Using the highest supported .NET version is recommended, since newer SDKs are capable of building for older target frameworks but not vice versa.
 
### Building
 
To install all required packages:

```
dotnet restore
```

Then, to build the library without running any tests:

```
dotnet build src/LaunchDarkly.InternalSdk
```

### Testing
 
To run all unit tests:

```
dotnet test test/LaunchDarkly.InternalSdk.Tests/LaunchDarkly.InternalSdk.Tests.csproj
```

Note that the unit tests can only be run in Debug configuration. There is an `InternalsVisibleTo` directive that allows the test code to access internal members of the library, and assembly strong-naming in the Release configuration interferes with this.
