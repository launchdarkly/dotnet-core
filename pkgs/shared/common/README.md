# LaunchDarkly SDK .NET Common Code

[![NuGet](https://img.shields.io/nuget/v/LaunchDarkly.CommonSdk.svg?style=flat-square)](https://www.nuget.org/packages/LaunchDarkly.CommonSdk/)
[![CircleCI](https://circleci.com/gh/launchdarkly/dotnet-sdk-common.svg?style=shield)](https://circleci.com/gh/launchdarkly/dotnet-sdk-common)

This project contains .NET classes and interfaces that are shared between the LaunchDarkly .NET server-side and client-side SDKs in this repository.

## Contributing

See [Contributing](./CONTRIBUTING.md).

## Signing

The published version of this assembly is digitally signed with Authenticode and [strong-named](https://docs.microsoft.com/en-us/dotnet/framework/app-domains/strong-named-assemblies). Building the code locally in the default Debug configuration does not use strong-naming and does not require a key file. The public key file is in this repository at `LaunchDarkly.CommonSdk.pk` as well as here:

```
Public Key:
0024000004800000940000000602000000240000525341310004000001000100
250509411af6d31f2abfc9b33d02b01c6ad14fd5c7f83cc6135f499ebb0ec8f3
4e05c59e49232f5a7d75d5761281610219d323043936d55c19bb26f1dd86bdc7
6ab178015e78b54aef9cbdc824db2afcf7250292ae3d8d9c4522bcc3a4fc4831
d4b4320e820f32e024ad50a786f86d37ea45e0c25ec431a7a0f3e93575a0d2ad

Public Key Token: 45ef1738a929a7df
```

## About LaunchDarkly

* LaunchDarkly is a continuous delivery platform that provides feature flags as a service and allows developers to iterate quickly and safely. We allow you to easily flag your features and manage them from the LaunchDarkly dashboard.  With LaunchDarkly, you can:
    * Roll out a new feature to a subset of your users (like a group of users who opt-in to a beta tester group), gathering feedback and bug reports from real-world use cases.
    * Gradually roll out a feature to an increasing percentage of users, and track the effect that the feature has on key metrics (for instance, how likely is a user to complete a purchase if they have feature A versus feature B?).
    * Turn off a feature that you realize is causing performance problems in production, without needing to re-deploy, or even restart the application with a changed configuration file.
    * Grant access to certain features based on user attributes, like payment plan (eg: users on the ‘gold’ plan get access to more features than users in the ‘silver’ plan). Disable parts of your application to facilitate maintenance, without taking everything offline.
* LaunchDarkly provides feature flag SDKs for a wide variety of languages and technologies. Check out [our documentation](https://docs.launchdarkly.com/sdk) for a complete list.
* Explore LaunchDarkly
    * [launchdarkly.com](https://www.launchdarkly.com/ "LaunchDarkly Main Website") for more information
    * [docs.launchdarkly.com](https://docs.launchdarkly.com/  "LaunchDarkly Documentation") for our documentation and SDK reference guides
    * [apidocs.launchdarkly.com](https://apidocs.launchdarkly.com/  "LaunchDarkly API Documentation") for our API documentation
    * [blog.launchdarkly.com](https://blog.launchdarkly.com/  "LaunchDarkly Blog Documentation") for the latest product updates
