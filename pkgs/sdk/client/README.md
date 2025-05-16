# LaunchDarkly Client-Side SDK for .NET

The LaunchDarkly Client-Side SDK for .NET is designed primarily for use by code that is deployed to an end user, such as in a desktop application or a smart device. It follows the client-side LaunchDarkly model for single-user contexts (much like our mobile or JavaScript SDKs). It is not intended for use in multi-user systems such as web servers and applications.

On platforms with MAUI support (Android, iOS, Mac, Windows), the SDK depends on the MAUI framework which allows .NET code to run on those devices.  However, MAUI is not the only way to run .NET code in a client-side context (see "Supported platforms" below), so the SDK has a more general name.

For using LaunchDarkly in *server-side* .NET applications, refer to our [Server-Side .NET SDK](../server).

## LaunchDarkly overview

[LaunchDarkly](https://www.launchdarkly.com) is a feature management platform that serves trillions of feature flags daily to help teams build better software, faster. [Get started](https://docs.launchdarkly.com/home/getting-started) using LaunchDarkly today!

## Supported platforms

This version of the SDK is built for the following targets:

* .Net Standard 2.0
* .Net 8 Android, for use with Android 5.0 (Android API 21) and higher.
* .Net 8 iOS, for use with iOS 11 and higher.
* .Net 8 macOS (using Mac Catalyst), for use with macOS 10.15 and higher.
* .Net 8 Windows (using WinUI), for Windows 11 and Windows 10 version 1809 or higher.
* .NET 8

The .Net Standard and .Net 8.0 targets have no OS-specific code. This allows the SDK to be used in a desktop .NET Framework or .NET 8.0 application. However, due to the lack of OS-specific integration, SDK functionality will be limited in those environments: for instance, the SDK will not be able to detect whether networking is turned on or off.

The .NET build tools should automatically load the most appropriate build of the SDK for whatever platform your application or library is targeted to.

## Known Issues

Currently UWP has an issue with streaming network requests. For more information, refer to this issue: https://github.com/dotnet/runtime/issues/108622
This may be resolved by .NET 9.0 UWP support.

## Getting started

Refer to the [SDK documentation](https://docs.launchdarkly.com/sdk/client-side/dotnet) for instructions on getting started with using the SDK.

## Learn more

Read our [documentation](https://docs.launchdarkly.com) for in-depth instructions on configuring and using LaunchDarkly. You can also head straight to the [complete reference guide for this SDK](https://docs.launchdarkly.com/sdk/client-side/dotnet).

The authoritative description of all types, properties, and methods is in the [generated API documentation](https://launchdarkly.github.io/dotnet-core/pkgs/sdk/client).

## Testing
 
We run integration tests for all our SDKs using a centralized test harness. This approach gives us the ability to test for consistency across SDKs, as well as test networking behavior in a long-running application. These tests cover each method in the SDK, and verify that event sending, flag evaluation, stream reconnection, and other aspects of the SDK all behave correctly.
 
## Contributing
 
We encourage pull requests and other contributions from the community. Check out our [contributing guidelines](CONTRIBUTING.md) for instructions on how to contribute to this SDK.

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
