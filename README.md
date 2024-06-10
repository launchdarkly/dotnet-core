# LaunchDarkly monorepo for .NET SDKs and C# libs.

This repository contains LaunchDarkly SDK packages for usage in .NET.
This includes the SDKs, shared libraries, and other tools.

## Packages

| SDK Packages                              | NuGet                                                   | API Docs                                                    | Issues                        | Tests                                                      |
|-------------------------------------------|---------------------------------------------------------|-------------------------------------------------------------|-------------------------------|------------------------------------------------------------|
| [LaunchDarkly.ServerSdk](pkgs/sdk/server) | [![NuGet][server-nuget-badge]][server-nuget-link]       | [![Documentation][api-docs-badge]][server-api-docs-link]    | [Server SDK][server-issues]   | [![Actions Status][server-ci-badge]][server-ci-link]       |
| [LaunchDarkly.ClientSdk](pkgs/sdk/client) | [![NuGet][client-nuget-badge]][client-nuget-link]       | [![Documentation][api-docs-badge]][client-api-docs-link]    | [Client SDK][client-issues]   | [![Actions Status][client-ci-badge]][client-ci-link]       |

| Telemetry Packages                                 | NuGet                                                   | API Docs                                                    | Issues                        | Tests                                                      |
|----------------------------------------------------|---------------------------------------------------------|-------------------------------------------------------------|-------------------------------|------------------------------------------------------------|
| [LaunchDarkly.ServerSdk.Telemetry](pkgs/telemetry) | [![NuGet][telemetry-nuget-badge]][telemetry-nuget-link] | [![Documentation][api-docs-badge]][telemetry-api-docs-link] | [Telemetry][telemetry-issues] | [![Actions Status][telemetry-ci-badge]][telemetry-ci-link] |

## Organization

| Directory        | Purpose                                                                                                    |
|------------------|------------------------------------------------------------------------------------------------------------|
| `pkgs`           | Top level directory containing package implementations.                                                    |
| `pkgs/sdk`       | SDK packages intended for use by application developers. Currently contains only the .NET Server-Side SDK. |
| `pkgs/telemetry` | Packages for adding telemetry support to SDKs.                                                             |

## LaunchDarkly overview

[LaunchDarkly](https://www.launchdarkly.com) is a feature management platform that serves trillions of feature flags daily to help teams build better software, faster. [Get started](https://docs.launchdarkly.com/home/getting-started) using LaunchDarkly today!
 
[![Twitter Follow](https://img.shields.io/twitter/follow/launchdarkly.svg?style=social&label=Follow&maxAge=2592000)](https://twitter.com/intent/follow?screen_name=launchdarkly)

## Learn more

Read our [documentation](https://docs.launchdarkly.com) for in-depth instructions on configuring and using LaunchDarkly. You can also head straight to the [complete reference guide for this SDK](https://docs.launchdarkly.com/sdk/server-side/dotnet).

## Testing
 
We run integration tests for all our SDKs using a centralized test harness. This approach gives us the ability to test for consistency across SDKs, as well as test networking behavior in a long-running application. These tests cover each method in the SDK, and verify that event sending, flag evaluation, stream reconnection, and other aspects of the SDK all behave correctly.
 
## Contributing
 
We encourage pull requests and other contributions from the community. Check out our [contributing guidelines](CONTRIBUTING.md) for instructions on how to contribute to this repository.

## About LaunchDarkly
 
* LaunchDarkly is a continuous delivery platform that provides feature flags as a service and allows developers to iterate quickly and safely. We allow you to easily flag your features and manage them from the LaunchDarkly dashboard.  With LaunchDarkly, you can:
    * Roll out a new feature to a subset of your users (like a group of users who opt-in to a beta tester group), gathering feedback and bug reports from real-world use cases.
    * Gradually roll out a feature to an increasing percentage of users, and track the effect that the feature has on key metrics (for instance, how likely is a user to complete a purchase if they have feature A versus feature B?).
    * Turn off a feature that you realize is causing performance problems in production, without needing to re-deploy, or even restart the application with a changed configuration file.
    * Grant access to certain features based on user attributes, like payment plan (eg: users on the ‘gold’ plan get access to more features than users in the ‘silver’ plan). Disable parts of your application to facilitate maintenance, without taking everything offline.
* LaunchDarkly provides feature flag SDKs for a wide variety of languages and technologies. Read [our documentation](https://docs.launchdarkly.com/sdk) for a complete list.
* Explore LaunchDarkly
    * [launchdarkly.com](https://www.launchdarkly.com/ "LaunchDarkly Main Website") for more information
    * [docs.launchdarkly.com](https://docs.launchdarkly.com/  "LaunchDarkly Documentation") for our documentation and SDK reference guides
    * [apidocs.launchdarkly.com](https://apidocs.launchdarkly.com/  "LaunchDarkly API Documentation") for our API documentation
    * [blog.launchdarkly.com](https://blog.launchdarkly.com/  "LaunchDarkly Blog Documentation") for the latest product updates


[server-nuget-badge]: https://img.shields.io/nuget/v/LaunchDarkly.ServerSdk.svg?style=flat-square
[server-nuget-link]: https://www.nuget.org/packages/LaunchDarkly.ServerSdk/
[server-ci-badge]: https://github.com/launchdarkly/dotnet-core/actions/workflows/sdk-server-ci.yml/badge.svg
[server-ci-link]: https://github.com/launchdarkly/dotnet-core/actions/workflows/sdk-server-ci.yml
[server-issues]: https://github.com/launchdarkly/dotnet-core/issues?q=is%3Aissue+is%3Aopen+label%3A%22package%3A+sdk%2Fserver%22+
[server-api-docs-link]: https://launchdarkly.github.io/dotnet-core/pkgs/sdk/server/

[client-nuget-badge]: https://img.shields.io/nuget/v/LaunchDarkly.ClientSdk.svg?style=flat-square
[client-nuget-link]: https://www.nuget.org/packages/LaunchDarkly.ClientSdk/
[client-ci-badge]: https://github.com/launchdarkly/dotnet-core/actions/workflows/sdk-client-ci.yml/badge.svg
[client-ci-link]: https://github.com/launchdarkly/dotnet-core/actions/workflows/sdk-client-ci.yml
[client-issues]: https://github.com/launchdarkly/dotnet-core/issues?q=is%3Aissue+is%3Aopen+label%3A%22package%3A+sdk%2Fclient%22+
[client-api-docs-link]: https://launchdarkly.github.io/dotnet-core/pkgs/sdk/client/

[telemetry-nuget-badge]: https://img.shields.io/nuget/v/LaunchDarkly.ServerSdk.Telemetry.svg?style=flat-square
[telemetry-nuget-link]: https://www.nuget.org/packages/LaunchDarkly.ServerSdk.Telemetry/
[telemetry-ci-badge]: https://github.com/launchdarkly/dotnet-core/actions/workflows/telemetry-ci.yml/badge.svg
[telemetry-ci-link]: https://github.com/launchdarkly/dotnet-core/actions/workflows/telemetry-ci.yml
[telemetry-issues]: https://github.com/launchdarkly/dotnet-core/issues?q=is%3Aissue+is%3Aopen+label%3A%22package%3A+telemetry%22+
[telemetry-api-docs-link]: https://launchdarkly.github.io/dotnet-core/pkgs/telemetry/

[api-docs-badge]: https://img.shields.io/static/v1?label=GitHub+Pages&message=API+reference&color=00add8
