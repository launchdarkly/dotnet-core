# LaunchDarkly AI SDK (server-side) for .NET

> [!CAUTION]
> This AI SDK is in pre-release and not subject to backwards compatibility guarantees. The API may change based on feedback.
>
> Pin to a specific minor version and review the [changelog] before upgrading.
>
> Active feature development is ongoing in the [Python][python-ai-sdk] and [Node.js][node-ai-sdk] AI SDKs, so this SDK will receive new features at a slower pace. Refer to those for the latest capabilities.

[changelog]: https://github.com/launchdarkly/dotnet-core/blob/main/pkgs/sdk/server-ai/CHANGELOG.md
[python-ai-sdk]: https://github.com/launchdarkly/python-server-sdk-ai/tree/main/packages/sdk/server-ai
[node-ai-sdk]: https://github.com/launchdarkly/js-core/tree/main/packages/sdk/server-ai


The LaunchDarkly AI SDK (server-side) for .NET is designed primarily for use in multi-user systems such as web servers and applications. It follows the server-side LaunchDarkly model for multi-user contexts. It is not intended for use in desktop and embedded systems applications.

Currently there is no client-side AI SDK for .NET. If you're interested, please let us know by filing an issue!

## LaunchDarkly overview

[LaunchDarkly](https://www.launchdarkly.com) is a feature management platform that serves trillions of feature flags daily to help teams build better software, faster. [Get started](https://docs.launchdarkly.com/home/getting-started) using LaunchDarkly today!
 
[![Twitter Follow](https://img.shields.io/twitter/follow/launchdarkly.svg?style=social&label=Follow&maxAge=2592000)](https://twitter.com/intent/follow?screen_name=launchdarkly)

## Supported .NET versions

This version of the AI SDK is built for the following targets:

* .NET 8.0: runs on .NET 8.0 and above (including higher major versions).
* .NET Framework 4.6.2: runs on .NET Framework 4.6.2 and above.
* .NET Standard 2.0: runs in any project that is targeted to .NET Standard 2.x rather than to a specific runtime platform.

The .NET build tools should automatically load the most appropriate build of the SDK for whatever platform your application or library is targeted to.

## Getting started

Refer to the [SDK documentation](https://docs.launchdarkly.com/sdk/ai/dotnet) for instructions on getting started with using the SDK.

## Signing

The published version of this assembly is digitally signed with Authenticode and [strong-named](https://docs.microsoft.com/en-us/dotnet/framework/app-domains/strong-named-assemblies). Building the code locally in the default Debug configuration does not use strong-naming and does not require a key file. The public key file is in this repository at `LaunchDarkly.pk` as well as here:

```
Public Key:
0024000004800000940000000602000000240000525341310004000001000100f121bbf427e4d7
edc64131a9efeefd20978dc58c285aa6f548a4282fc6d871fbebeacc13160e88566f427497b625
56bf7ff01017b0f7c9de36869cc681b236bc0df0c85927ac8a439ecb7a6a07ae4111034e03042c
4b1569ebc6d3ed945878cca97e1592f864ba7cc81a56b8668a6d7bbe6e44c1279db088b0fdcc35
52f746b4

Public Key Token: f86add69004e6885
```

## Learn more

Read our [documentation](https://docs.launchdarkly.com) for in-depth instructions on configuring and using LaunchDarkly. You can also head straight to the [complete reference guide for this SDK](https://docs.launchdarkly.com/sdk/ai/dotnet).

The authoritative description of all types, properties, and methods is in the [generated API documentation](https://launchdarkly.github.io/dotnet-core/pkgs/sdk/server-ai).

## Contributing
 
We encourage pull requests and other contributions from the community. Check out our [contributing guidelines](CONTRIBUTING.md) for instructions on how to contribute to this SDK.

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
