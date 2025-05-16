# Change log

All notable changes to the LaunchDarkly .NET SDK Consul integration will be documented in this file. This project adheres to [Semantic Versioning](http://semver.org).

## [5.0.0] - 2023-10-16
### Changed:
- This release requires the `8.0.0` release of the `LaunchDarkly.ServerSdk`

## [4.0.0] - 2022-12-07
This release corresponds to the 7.0.0 release of the LaunchDarkly server-side .NET SDK. Any application code that is being updated to use the 7.0.0 SDK, and was using a 3.x version of `LaunchDarkly.ServerSdk.Consul`, should now use a 4.x version instead.

There are no functional differences in the behavior of the Consul integration; the differences are only related to changes in the usage of interface types for configuration in the SDK.

### Changed:
- In `DataStoreBuilder`, the method `CreatePersistentDataStore` has been renamed to `Build`, corresponding to changes in how the SDK uses interface types for configuration. Application code would not normally reference this method.

## [3.0.0] - 2022-10-24
This release updates the integration to use the current 1.x stable version of the [Consul.NET](https://www.nuget.org/packages/Consul) client package. Previously, that package had a different maintainer and did not have a stable version.

Because Consul.NET does not support .NET Framework 4.5.2, this integration now has a minimum .NET Framework version of 4.6.1 (which is the reason for the 3.0.0 major version increment in this release). Its functionality is otherwise unchanged.

## [2.0.0] - 2021-06-09
This release is for use with versions 6.0.0 and higher of [`LaunchDarkly.ServerSdk`](https://github.com/launchdarkly/dotnet-server-sdk).

For more information about changes in the SDK database integrations, see the [5.x to 6.0 migration guide](https://docs-stg.launchdarkly.com/252/sdk/server-side/dotnet/migration-5-to-6).

### Added:
- Added an overload of `ConsulDataStoreBuilder.Address` that takes a `string` rather than a `Uri`.

### Changed:
- The namespace is now `LaunchDarkly.Sdk.Server.Integrations`.
- The entry point is now `LaunchDarkly.Sdk.Server.Integrations.Consul` rather than `LaunchDarkly.Client.Integrations.Consul` (or, in earlier versions, `LaunchDarkly.Client.Consul.ConsulComponents`).
- If you pass in an existing Consul client instance with `ConsulDataStoreBuilder.ExistingClient`, the SDK will no longer dispose of the client on shutdown; you are responsible for its lifecycle.
- The logger name is now `LaunchDarkly.Sdk.DataStore.Consul` rather than `LaunchDarkly.Client.Consul.ConsulFeatureStoreCore`.

### Removed:
- Removed the deprecated `ConsulComponents` entry point and `ConsulFeatureStoreBuilder`.
- The package no longer has a dependency on `Common.Logging` but instead integrates with the SDK&#39;s logging mechanism.


## [1.1.0] - 2021-01-26
### Added:
- New classes `LaunchDarkly.Client.Integrations.Consul` and `LaunchDarkly.Client.Integrations.ConsulDataStoreBuilder`, which serve the same purpose as the previous classes but are designed to work with the newer persistent data store API introduced in .NET SDK 5.14.0.

### Deprecated:
- The old API in the `LaunchDarkly.Client.Consul` namespace.

## [1.0.1] - 2019-05-10
### Changed:
- Corresponding to the SDK package name change from `LaunchDarkly.Client` to `LaunchDarkly.ServerSdk`, this package is now called `LaunchDarkly.ServerSdk.Consul`. The functionality of the package, including the namespaces and class names, has not changed.

## [1.0.0] - 2019-01-11

Initial release.
