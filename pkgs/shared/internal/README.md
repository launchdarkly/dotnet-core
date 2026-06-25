# LaunchDarkly SDK .NET Internal Common Code

[![NuGet](https://img.shields.io/nuget/v/LaunchDarkly.InternalSdk.svg?style=flat-square)](https://www.nuget.org/packages/LaunchDarkly.InternalSdk/)

This project contains .NET classes and interfaces that are shared between the LaunchDarkly .NET and Xamarin SDKs. These are internal implementation details that are not part of the supported SDK APIs and should not be used by application code. Code that is specific to one or the other SDK is in [dotnet-server-sdk](https://github.com/launchdarkly/dotnet-server-sdk) or [xamarin-client-sdk](https://github.com/launchdarkly/xamarin-client-sdk), and public APIs that are common to both are in [dotnet-sdk-common](https://github.com/launchdarkly/dotnet-sdk-common).

## Contributing

See [Contributing](https://github.com/launchdarkly/dotnet-sdk-internal/blob/main/CONTRIBUTING.md).

## Signing

The published version of this assembly is digitally signed with Authenticode and [strong-named](https://docs.microsoft.com/en-us/dotnet/framework/app-domains/strong-named-assemblies). Building the code locally in the default Debug configuration does not use strong-naming and does not require a key file. The public key file is in this repository at `LaunchDarkly.InternalSdk.pk` as well as here:

```
Public Key:
0024000004800000940000000602000000240000525341310004000001000100
c750d2f66590e46bab94497b8df2a773ce941140566e4b4e532e921dd0ccb0c2
ddc934dc4dbcb14fc48c4d75ff5fc43ef3ed83f67fb20061a5ea83b656eded02
7f489ca157213634506ed8a5dce2f9582edfc4bb2cbf2a9c61bc78f8aacd4b3c
79397cddfa058c3b538c294eb29d05f72e710343e714b4d5b3f8f8b12483d68e

Public Key Token: ff53908ab73043b6
```

## Verifying build provenance with the SLSA framework

LaunchDarkly uses the [SLSA framework](https://slsa.dev/spec/v1.0/about) (Supply-chain Levels for Software Artifacts) to help developers make their supply chain more secure by ensuring the authenticity and build integrity of our published packages. To learn more, see the [provenance guide](PROVENANCE.md).

## About LaunchDarkly
 
* LaunchDarkly is a continuous delivery platform that provides feature flags as a service and allows developers to iterate quickly and safely. We allow you to easily flag your features and manage them from the LaunchDarkly dashboard.  With LaunchDarkly, you can:
    * Roll out a new feature to a subset of your users (like a group of users who opt-in to a beta tester group), gathering feedback and bug reports from real-world use cases.
    * Gradually roll out a feature to an increasing percentage of users, and track the effect that the feature has on key metrics (for instance, how likely is a user to complete a purchase if they have feature A versus feature B?).
    * Turn off a feature that you realize is causing performance problems in production, without needing to re-deploy, or even restart the application with a changed configuration file.
    * Grant access to certain features based on user attributes, like payment plan (eg: users on the ‘gold’ plan get access to more features than users in the ‘silver’ plan). Disable parts of your application to facilitate maintenance, without taking everything offline.
* LaunchDarkly provides feature flag SDKs for a wide variety of languages and technologies. Check out [our documentation](https://docs.launchdarkly.com/docs) for a complete list.
* Explore LaunchDarkly
    * [launchdarkly.com](https://www.launchdarkly.com/ "LaunchDarkly Main Website") for more information
    * [docs.launchdarkly.com](https://docs.launchdarkly.com/  "LaunchDarkly Documentation") for our documentation and SDK reference guides
    * [apidocs.launchdarkly.com](https://apidocs.launchdarkly.com/  "LaunchDarkly API Documentation") for our API documentation
    * [blog.launchdarkly.com](https://blog.launchdarkly.com/  "LaunchDarkly Blog Documentation") for the latest product updates
