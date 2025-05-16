# LaunchDarkly Server-Side SDK for .NET - DynamoDB integration

[![NuGet](https://img.shields.io/nuget/v/LaunchDarkly.ServerSdk.DynamoDB.svg?style=flat-square)](https://www.nuget.org/packages/LaunchDarkly.ServerSdk.DynamoDB/)
[![Build and Test](https://github.com/launchdarkly/dotnet-server-sdk-dynamodb/actions/workflows/ci.yml/badge.svg)](https://github.com/launchdarkly/dotnet-server-sdk-dynamodb/actions/workflows/ci.yml)
[![Documentation](https://img.shields.io/static/v1?label=GitHub+Pages&message=API+reference&color=00add8)](https://launchdarkly.github.io/dotnet-server-sdk-dynamodb)

This library provides a DynamoDB-backed persistence mechanism (data store) for the [LaunchDarkly server-side .NET SDK](https://github.com/launchdarkly/dotnet-server-sdk), replacing the default in-memory data store. It uses the [AWS SDK for .NET](https://aws.amazon.com/sdk-for-net/).

For more information, see also: [Using DynamoDB as a persistent feature store](https://docs.launchdarkly.com/sdk/features/storing-data/dynamodb#net).

Version 4.0.0 and above of this library works with version 8.0.0 and above of the LaunchDarkly .NET SDK. For earlier versions of the SDK, see the changelog for which version of this library to use.

For full usage details and examples, see the [API reference](launchdarkly.github.io/dotnet-server-sdk-dynamodb).

## .NET platform compatibility

This version of the library is built for the following targets:

* .NET Framework 4.6.2: works in .NET Framework of that version or higher.
* .NET Standard 2.0: works in .NET Core 3.x, .NET 6.x, or in a library targeted to .NET Standard 2.x.

The .NET build tools should automatically load the most appropriate build of the library for whatever platform your application or library is targeted to.

## Data size limitation

DynamoDB has [a 400KB limit](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/ServiceQuotas.html#limits-items) on the size of any data item. For the LaunchDarkly SDK, a data item consists of the JSON representation of an individual feature flag or segment configuration, plus a few smaller attributes. You can see the format and size of these representations by querying `https://sdk.launchdarkly.com/flags/latest-all` and setting the `Authorization` header to your SDK key.

Most flags and segments won't be nearly as big as 400KB, but they could be if for instance you have a long list of user keys for individual user targeting. If the flag or segment representation is too large, it cannot be stored in DynamoDB. To avoid disrupting storage and evaluation of other unrelated feature flags, the SDK will simply skip storing that individual flag or segment, and will log a message (at ERROR level) describing the problem. For example:

```
    The item "my-flag-key" in "features" was too large to store in DynamoDB and was dropped
```

If caching is enabled in your configuration, the flag or segment may still be available in the SDK from the in-memory cache, but do not rely on this. If you see this message, consider redesigning your flag/segment configurations, or else do not use DynamoDB for the environment that contains this data item.

This limitation does not apply to target lists in [Big Segments](https://docs.launchdarkly.com/home/users/big-segments/).

A future version of the LaunchDarkly DynamoDB integration may use different strategies to work around this limitation, such as compressing the data or dividing it into multiple items. However, this integration is required to be interoperable with the DynamoDB integrations used by all the other LaunchDarkly SDKs and by the Relay Proxy, so any such change will only be made as part of a larger cross-platform release.

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
