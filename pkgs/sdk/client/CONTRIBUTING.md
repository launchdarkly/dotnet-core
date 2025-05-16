# Contributing to the LaunchDarkly Client-Side SDK for .NET

LaunchDarkly has published an [SDK contributor's guide](https://docs.launchdarkly.com/docs/sdk-contributors-guide) that provides a detailed explanation of how our SDKs work. See below for additional information on how to contribute to this SDK.

## Submitting bug reports and feature requests

The LaunchDarkly SDK team monitors the [issue tracker](https://github.com/launchdarkly/dotnet-core/issues) in the SDK repository. Bug reports and feature requests specific to this SDK should be filed in this issue tracker. The SDK team will respond to all newly filed issues within two business days.

## Submitting pull requests

We encourage pull requests and other contributions from the community. Before submitting pull requests, ensure that all temporary or unintended code is removed. Don't worry about adding reviewers to the pull request; the LaunchDarkly SDK team will add themselves. The SDK team will acknowledge all pull requests within two business days.

## Build instructions

### Prerequisites

The .NET Standard target requires only the .NET Core 2.1 SDK or higher. The iOS, Android, MacCatalys, and Windows targets require Net 7.0 or later.

### Building

To build the SDK (for all target platforms) without running any tests:

```
dotnet workload restore src/LaunchDarkly.ClientSdk.csproj
dotnet restore src/LaunchDarkly.ClientSdk.csproj
dotnet build src/LaunchDarkly.ClientSdk.csproj
```

Currently this command can only be run on MacOS, because that is the only platform that allows building for all of the targets (.NET Standard, Android, and iOS).

To build the SDK for only one of the supported platforms, add `/p:TargetFramework=X` where `X` is one of the items in the `<TargetFrameworks>` list of `LaunchDarkly.ClientSdk.csproj`: `netstandard2.0` for .NET Standard 2.0, `net8.0-android` for Android, etc.:

```
dotnet build /p:TargetFramework=net8.0-ios src/LaunchDarkly.ClientSdk.csproj
```

Note that the main project, `src/LaunchDarkly.ClientSdk`, contains source files that are built for all platforms (ending in just `.cs`, or `.shared.cs`), and also a smaller amount of code that is conditionally compiled for platform-specific functionality. The latter is all in the `PlatformSpecific` folder. We use `#ifdef` directives only for small sections that differ slightly between platform versions; otherwise the conditional compilation is done according to filename suffix (`.android.cs`, etc.) based on rules in the `.csproj` file.

### Testing

The .NET Standard unit tests cover all of the non-platform-specific functionality, as well as behavior specific to .NET Standard (e.g. caching flags in the filesystem). They can be run with only the basic Xamarin framework installed, via the `dotnet` tool:

```
dotnet build src/LaunchDarkly.ClientSdk.csproj
dotnet test tests/LaunchDarkly.ClientSdk.Tests/LaunchDarkly.ClientSdk.Tests.csproj
```

To run the SDK contract test suite, in Linux or MacOS (see [`contract-tests/README.md`](./contract-tests/README.md)):

```bash
make contract-tests
```

The equivalent test suites for MAUI (Android or iOS) must be run in an Android or iOS emulator. The project `test/LaunchDarkly.ClientSdk.Device.Tests` consist of a test applications based on the `xunit.runner.devices` tool, which show the test results visually in the emulator and also write the results to the emulator's system log. The actual unit test code is just the same tests from the main `tests/LaunchDarkly.ClientSdk.Tests` project, but running them in this way exercises the mobile-specific behavior for those platforms (e.g. caching flags in user preferences).

You can run the mobile test projects from Visual Studio (the iOS tests require MacOS).

Note that the mobile unit tests currently do not cover background-mode behavior or connectivity detection.
