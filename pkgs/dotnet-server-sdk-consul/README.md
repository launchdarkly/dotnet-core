# LaunchDarkly Server-Side SDK for .NET - Consul integration

[![NuGet](https://img.shields.io/nuget/v/LaunchDarkly.ServerSdk.Consul.svg?style=flat-square)](https://www.nuget.org/packages/LaunchDarkly.ServerSdk.Consul/)
[![CircleCI](https://circleci.com/gh/launchdarkly/dotnet-server-sdk-consul.svg?style=shield)](https://circleci.com/gh/launchdarkly/dotnet-server-sdk-consul)
[![Documentation](https://img.shields.io/static/v1?label=GitHub+Pages&message=API+reference&color=00add8)](https://launchdarkly.github.io/dotnet-server-sdk-consul)

This library provides a Consul-backed persistence mechanism (data store) for the [LaunchDarkly server-side .NET SDK](https://github.com/launchdarkly/dotnet-server-sdk), replacing the default in-memory data store. It uses the open-source [Consul.NET](https://www.nuget.org/packages/Consul) package.

For more information, see also: [Using Consul as a persistent feature store](https://docs.launchdarkly.com/sdk/features/storing-data/consul#net-server-side).

Version 5.0.0 and above of this library works with version 8.0.0 and above of the LaunchDarkly .NET SDK. For earlier versions of the SDK, see the changelog for which version of this library to use.

For full usage details and examples, see the [API reference](launchdarkly.github.io/dotnet-server-sdk-consul).

## Supported .NET versions

This version of the library is built for the following targets:

* .NET Framework 4.6.2: works in .NET Framework of that version or higher.
* .NET Standard 2.0: works in .NET Core 3.1, .NET 6 or higher, or in a library targeted to .NET Standard 2.x.

The .NET build tools should automatically load the most appropriate build of the library for whatever platform your application or library is targeted to.

It has a dependency on version 1.6.1.1 of [Consul.NET](https://www.nuget.org/packages/Consul). If you are using a higher version of that package, you should install it explicitly as a dependency in your application to override this minimum version.

## Signing

The published version of this assembly is digitally signed with Authenticode and [strong-named](https://docs.microsoft.com/en-us/dotnet/framework/app-domains/strong-named-assemblies). Building the code locally in the default Debug configuration does not use strong-naming and does not require a key file.

## Contributing

We encourage pull requests and other contributions from the community. Check out our [contributing guidelines](CONTRIBUTING.md) for instructions on how to contribute to this project.

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
