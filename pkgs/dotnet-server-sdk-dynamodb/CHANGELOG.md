# Change log

All notable changes to the LaunchDarkly .NET SDK DynamoDB integration will be documented in this file. This project adheres to [Semantic Versioning](http://semver.org).

## [4.0.1](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.DynamoDb-v4.0.0...LaunchDarkly.ServerSdk.DynamoDb-v4.0.1) (2025-10-21)


### Bug Fixes

* Prevent using incompatible AWS version ([#171](https://github.com/launchdarkly/dotnet-core/issues/171)) ([5496a64](https://github.com/launchdarkly/dotnet-core/commit/5496a64bedc2bc25fdf8ca9757345de25b67f38f))

## [4.0.0] - 2023-10-16
### Changed:
- This release requires the `8.0.0` release of the `LaunchDarkly.ServerSdk`.

## [3.0.0] - 2022-12-07
This release corresponds to the 7.0.0 release of the LaunchDarkly server-side .NET SDK. Any application code that is being updated to use the 7.0.0 SDK, and was using a 2.x version of `LaunchDarkly.ServerSdk.DynamoDb`, should now use a 3.x version instead.

There are no functional differences in the behavior of the DynamoDB integration; the differences are only related to changes in the usage of interface types for configuration in the SDK.

### Added:
- `DynamoDb.BigSegmentStore()`, which creates a configuration builder for use with Big Segments. Previously, the `DynamoDb.DataStore()` builder was used for both regular data stores and Big Segment stores.

### Changed:
- The type `DynamoDbDataStoreBuilder` has been removed, replaced by a generic type `DynamoDbStoreBuilder`. Application code would not normally need to reference these types by name, but if necessary, use either `DynamoDbStoreBuilder<PersistentDataStore>` or `DynamoDbStoreBuilder<BigSegmentStore>` depending on whether you are configuring a regular data store or a Big Segment store.

## [2.1.1] - 2022-04-19
### Fixed:
- If the SDK attempts to store a feature flag or segment whose total data size is over the 400KB limit for DynamoDB items, this integration will now log (at Error level) a message like `The item "my-flag-key" in "features" was too large to store in DynamoDB and was dropped` but will still process all other data updates. Previously, it would cause the SDK to enter an error state in which the oversized item would be pointlessly retried and other updates might be lost.

## [2.1.0] - 2021-07-22
### Added:
- Added support for Big Segments. An Early Access Program for creating and syncing Big Segments from customer data platforms is available to enterprise customers.

## [2.0.0] - 2021-06-09
This release is for use with versions 6.0.0 and higher of [`LaunchDarkly.ServerSdk`](https://github.com/launchdarkly/dotnet-server-sdk).

For more information about changes in the SDK database integrations, see the [5.x to 6.0 migration guide](https://docs-stg.launchdarkly.com/252/sdk/server-side/dotnet/migration-5-to-6).

### Changed:
- The namespace is now `LaunchDarkly.Sdk.Server.Integrations`.
- The entry point is now `LaunchDarkly.Sdk.Server.Integrations.DynamoDB` rather than `LaunchDarkly.Client.Integrations.DynamoDB` (or, in earlier versions, `LaunchDarkly.Client.DynamoDB.DynamoDBComponents`).
- If you pass in an existing DynamoDB client instance with `DynamoDBDataStoreBuilder.ExistingClient`, the SDK will no longer dispose of the client on shutdown; you are responsible for its lifecycle.
- The logger name is now `LaunchDarkly.Sdk.DataStore.DynamoDB` rather than `LaunchDarkly.Client.DynamoDB.DynamoDBFeatureStoreCore`.

### Removed:
- Removed the deprecated `DynamoDBComponents` entry point and `DynamoDBFeatureStoreBuilder`.
- The package no longer has a dependency on `Common.Logging` but instead integrates with the SDK&#39;s logging mechanism.

## [1.1.0] - 2021-01-26
### Added:
- New classes `LaunchDarkly.Client.Integrations.DynamoDB` and `LaunchDarkly.Client.Integrations.DynamoDBStoreBuilder`, which serve the same purpose as the previous classes but are designed to work with the newer persistent data store API introduced in .NET SDK 5.14.0.

### Deprecated:
- The old API in the `LaunchDarkly.Client.DynamoDB` namespace.

## [1.0.1] - 2019-05-10
### Changed:
- Corresponding to the SDK package name change from `LaunchDarkly.Client` to `LaunchDarkly.ServerSdk`, this package is now called `LaunchDarkly.ServerSdk.DynamoDB`. The functionality of the package, including the namespaces and class names, has not changed.

## [1.0.0] - 2019-01-11

Initial release.
