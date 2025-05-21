# Change log

All notable changes to the LaunchDarkly .NET SDK Redis integration will be documented in this file. This project adheres to [Semantic Versioning](http://semver.org).

## [5.0.0] - 2023-10-16
### Changed:
- This release requires the `8.0.0` release of the `LaunchDarkly.ServerSdk`.

## [4.0.0] - 2022-12-07
This release corresponds to the 7.0.0 release of the LaunchDarkly server-side .NET SDK. Any application code that is being updated to use the 7.0.0 SDK, and was using a 3.x version of `LaunchDarkly.ServerSdk.Redis`, should now use a 4.x version instead.

There are no functional differences in the behavior of the Redis integration; the differences are only related to changes in the usage of interface types for configuration in the SDK.

### Added:
- `Redis.BigSegmentStore()`, which creates a configuration builder for use with Big Segments. Previously, the `Redis.DataStore()` builder was used for both regular data stores and Big Segment stores.

### Changed:
- The type `RedisDataStoreBuilder` has been removed, replaced by a generic type `RedisStoreBuilder`. Application code would not normally need to reference these types by name, but if necessary, use either `RedisStoreBuilder<PersistentDataStore>` or `RedisStoreBuilder<BigSegmentStore>` depending on whether you are configuring a regular data store or a Big Segment store.

## [3.1.0] - 2021-07-22
### Added:
- Added support for Big Segments. An Early Access Program for creating and syncing Big Segments from customer data platforms is available to enterprise customers.

## [3.0.0] - 2021-06-09
This release is for use with versions 6.0.0 and higher of [`LaunchDarkly.ServerSdk`](https://github.com/launchdarkly/dotnet-server-sdk).

For more information about changes in the SDK database integrations, see the [5.x to 6.0 migration guide](https://docs-stg.launchdarkly.com/252/sdk/server-side/dotnet/migration-5-to-6).

Like the previous major version of this library, it uses version 2.x of `StackExchange.Redis`.

### Changed:
- The namespace is now `LaunchDarkly.Sdk.Server.Integrations`.
- The entry point is now `LaunchDarkly.Sdk.Server.Integrations.Redis` rather than `LaunchDarkly.Client.Integrations.Redis` (or, in earlier versions, `LaunchDarkly.Client.Redis.RedisComponents`).
- The logger name is now `LaunchDarkly.Sdk.DataStore.Redis` rather than `LaunchDarkly.Client.Redis.RedisFeatureStoreCore`.

### Removed:
- Removed the deprecated `RedisComponents` entry point and `RedisFeatureStoreBuilder`.
- The package no longer has a dependency on `Common.Logging` but instead integrates with the SDK&#39;s logging mechanism.

## [2.0.1] - 2021-06-01
### Fixed:
- The library was not fully compliant with the standard usage of Redis keys by other LaunchDarkly SDKs and by the Relay Proxy, as follows: although feature flag data was stored with the correct keys, the wrong key was used for the special value that indicates that the database has been initialized. As a result, if the Relay Proxy had stored data in Redis, the .NET SDK would not detect it, and if the .NET SDK had stored data in Redis, other SDKs might not detect it.

## [2.0.0] - 2021-02-01
This release updates the third-party dependency on `StackExchange.Redis` to use the 2.x version of that library. For details about how `StackExchange.Redis` 2.x differs from the 1.x versions, see its [release notes](https://stackexchange.github.io/StackExchange.Redis/ReleaseNotes.html).

This version of `LaunchDarkly.ServerSdk.Redis` requires version 5.14 or higher of the LaunchDarkly .NET SDK (`LaunchDarkly.ServerSdk`). It supports both the older configuration API used in previous versions, and the newer configuration API that was introduced in version 5.14 of the SDK and in the [1.2.0](https://github.com/launchdarkly/dotnet-server-sdk-redis/releases/tag/1.2.0) release of this package. Using the newer API (see `LaunchDarkly.Client.Integrations.Redis` in this package) is preferable because that is how configuration will work in the 6.0 release of the SDK.

### Added:
- The `OperationTimeout` configuration property, which corresponds to `SyncTimeout` in the `StackExchange.Redis` API.

### Changed:
- The minimum version of `StackExchange.Redis` is now 2.0.513.
- The minimum version of `LaunchDarkly.ServerSdk` is now 5.14.0.
- There is no longer a separate `LaunchDarkly.ServerSdk.Redis.StrongName` package that is the strong-named version; instead, there is just `LaunchDarkly.ServerSdk.Redis` which is always strong-named. That distinction was previously necessary because the `StackExchange.Redis` package had both strong-named and non-strong-named versions, which is no longer the case.
- The lowest compatible version of .NET Framework is now 4.6.1 (because that is the lowest version supported by `StackExchange.Redis` 2.x). The package still has a .NET Standard 2.0 target as well.

### Removed:
- The `ResponseTimeout` configuration property, which is no longer supported by `StackExchange.Redis`.

## [1.2.1] - 2021-06-01
### Fixed:
- The library was not fully compliant with the standard usage of Redis keys by other LaunchDarkly SDKs and by the Relay Proxy, as follows: although feature flag data was stored with the correct keys, the wrong key was used for the special value that indicates that the database has been initialized. As a result, if the Relay Proxy had stored data in Redis, the .NET SDK would not detect it, and if the .NET SDK had stored data in Redis, other SDKs might not detect it.

## [1.2.0] - 2021-01-26
### Added:
- New classes `LaunchDarkly.Client.Integrations.Redis` and `LaunchDarkly.Client.Integrations.RedisDataStoreBuilder`, which serve the same purpose as the previous classes but are designed to work with the newer persistent data store API introduced in .NET SDK 5.14.0.

### Deprecated:
- The old API in the `LaunchDarkly.Client.Redis` namespace.

## [1.1.1] - 2019-05-13
### Changed:
- Corresponding to the SDK package name change from `LaunchDarkly.Client` to `LaunchDarkly.ServerSdk`, this package is now called `LaunchDarkly.ServerSdk.Redis` (or `LaunchDarkly.ServerSdk.Redis.StrongName`). The functionality of the package, including the namespaces and class names, has not changed.

## [1.1.0] - 2019-01-14
### Added
- `RedisFeatureStoreBuilder.WithCaching` is the new way to configure local caching behavior, using the new SDK class `FeatureStoreCacheConfig`. This allows you to specify a limit on the number of cached items, which was not previously possible. Future releases of the SDK may add more caching parameters, which will then be automatically supported by this library.
- The assemblies in this package now have Authenticode signatures.

### Changed
- The minimum LaunchDarkly.Client version for use with this library is now 5.6.1.

### Deprecated
- `RedisFeatureStoreBuilder.WithCacheExpiration`

## [1.0.0] - 2018-09-28

Initial release.
