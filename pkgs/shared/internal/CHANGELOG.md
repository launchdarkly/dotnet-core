# Change log

All notable changes to `LaunchDarkly.InternalSdk` will be documented in this file. For full release notes for the projects that depend on this project, see their respective changelogs. This file describes changes only to the common code. This project adheres to [Semantic Versioning](http://semver.org).

## [3.8.0](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.7.0...3.8.0) (2026-06-16)


### Features

* Update target frameworks to net8.0 ([#69](https://github.com/launchdarkly/dotnet-sdk-internal/issues/69)) ([235c52f](https://github.com/launchdarkly/dotnet-sdk-internal/commit/235c52f7e83db46590b6d4ac81333e555d7e876d))

## [3.7.0](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.6.1...3.7.0) (2026-06-08)


### Features

* add optional support for per-context summary events ([#65](https://github.com/launchdarkly/dotnet-sdk-internal/issues/65)) ([d835d08](https://github.com/launchdarkly/dotnet-sdk-internal/commit/d835d08d062d6d71acebc6b572d112eed976a508))

## [3.6.1](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.6.0...3.6.1) (2026-04-09)


### Bug Fixes

* Update CommonSdk to 7.2.0 ([#60](https://github.com/launchdarkly/dotnet-sdk-internal/issues/60)) ([047a867](https://github.com/launchdarkly/dotnet-sdk-internal/commit/047a867705a4bb4fd0f9cad99e9a51ed733952ef))

## [3.6.0](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.5.5...3.6.0) (2025-12-16)


### Features

* adds headers to UnsuccessfulResponseException ([#52](https://github.com/launchdarkly/dotnet-sdk-internal/issues/52)) ([3cca2ff](https://github.com/launchdarkly/dotnet-sdk-internal/commit/3cca2ff29e779def2fe75770dfdd13b94433742f))

## [3.5.5](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.5.4...3.5.5) (2025-09-26)


### Bug Fixes

* Update CommonSdk to 7.1.1 ([#50](https://github.com/launchdarkly/dotnet-sdk-internal/issues/50)) ([b8d57d3](https://github.com/launchdarkly/dotnet-sdk-internal/commit/b8d57d30032a26fd3b034dcea00e24af4cc3be68))

## [3.5.4](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.5.3...3.5.4) (2025-08-22)


### Bug Fixes

* Prevent invalid strings from being part of format value ([#46](https://github.com/launchdarkly/dotnet-sdk-internal/issues/46)) ([b4c772e](https://github.com/launchdarkly/dotnet-sdk-internal/commit/b4c772e059a1f67c8c94c5c39dbb44ca31c2850e))

## [3.5.3](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.5.2...3.5.3) (2025-07-10)


### Bug Fixes

* Relax common dependency ([#44](https://github.com/launchdarkly/dotnet-sdk-internal/issues/44)) ([66913b1](https://github.com/launchdarkly/dotnet-sdk-internal/commit/66913b15b173cbb4eb4a55133989a5f3c21c6781))

## [3.5.2](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.5.1...3.5.2) (2025-06-24)


### Bug Fixes

* Address ARM64 optimization throwing exceptions ([#42](https://github.com/launchdarkly/dotnet-sdk-internal/issues/42)) ([bac4833](https://github.com/launchdarkly/dotnet-sdk-internal/commit/bac4833a526b9f63025a0c864ce41514e402718c))

## [3.5.1](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.5.0...3.5.1) (2025-05-30)


### Bug Fixes

* conditionalize immutable dependency ([#40](https://github.com/launchdarkly/dotnet-sdk-internal/issues/40)) ([3ab529d](https://github.com/launchdarkly/dotnet-sdk-internal/commit/3ab529d730fcbcc0fd5614d96997929b2e4644e1))

## [3.5.0](https://github.com/launchdarkly/dotnet-sdk-internal/compare/3.4.0...3.5.0) (2025-05-02)


### Features

* Inline context for custom and migrations op events ([#34](https://github.com/launchdarkly/dotnet-sdk-internal/issues/34)) ([7013bbe](https://github.com/launchdarkly/dotnet-sdk-internal/commit/7013bbe95b3be44ca277f311a84e195e1adfd41d))

## [3.4.0] - 2024-03-13
### Changed:
- Redact anonymous attributes within feature events
- Always inline contexts for feature events

## [3.3.1] - 2023-10-17
### Changed:
- Updated Dotnet Common to 7.0.0 which contains nullability of IEnvironmentReporter properties.

## [3.3.0] - 2023-10-10
### Changed:
- Updated LaunchDarkly.CommonSdk to 6.2.0 to incorporate changes.

## [3.2.0] - 2023-10-10
### Added:
- Add common support for technology migrations.
- HttpProperties now supports WithApplicationTags.

## [3.1.2] - 2023-04-21
### Changed:
- Updated `LaunchDarkly.CommonSdk` to `6.0.1`.

## [3.1.1] - 2023-03-08
### Fixed:
- Fixed an issue where calling `FlushAndWait` with `TimeSpan.Zero` would never complete if there were no events to flush.

## [3.1.0] - 2022-12-06
### Added:
- In `EventProcessor`, `FlushAndWait` and `FlushAndWaitAsync`.

## [3.0.0] - 2022-12-01
This major version release of `LaunchDarkly.InternalSdk` corresponds to the upcoming v7.0.0 release of the LaunchDarkly server-side .NET SDK (`LaunchDarkly.ServerSdk`) and the v3.0.0 release of the LaunchDarkly client-side .NET SDK (`LaunchDarkly.ClientSdk`), and cannot be used with earlier SDK versions.

### Changed:
- .NET Core 2.1, .NET Framework 4.5.2, .NET Framework 4.6.1, and .NET 5.0 are now unsupported. The minimum platform versions are now .NET Core 3.1, .NET Framework 4.6.2, .NET 6.0, and .NET Standard 2.0.
- Events now use the `Context` type rather than `User`.
- Private attributes can now be designated with the `AttributeRef` type, which allows redaction of either a full attribute or a property within a JSON object value.
- There is a new JSON schema for analytics events. The HTTP headers for event payloads now report the schema version as 4.
- There is no longer a dependency on `LaunchDarkly.JsonStream`. This package existed because some platforms did not support the `System.Text.Json` API, but that is no longer the case and the SDK now uses `System.Text.Json` directly for all of its JSON operations.
- `EventSender` now takes a byte array instead of a string for the event payload, so we can serialize JSON data directly to UTF8.

### Removed:
- All alias event functionality
- `EventsConfiguration.InlineUsersInEvents`

## [2.3.2] - 2022-02-02
### Changed:
- Updated `LaunchDarkly.CommonSdk` dependency to latest release.

## [2.3.1] - 2022-01-28
### Fixed:
- In analytics event data, `index` events were showing a `contextKind` property for anonymous users. That type of event should not have that property; LaunchDarkly would ignore it.

## [2.3.0] - 2021-10-27
### Added:
- `HttpProperties.NewHttpMessageHandler()`

## [2.2.0] - 2021-10-25
### Added:
- In `TaskExecutor`, there is a new parameter for controlling how events are dispatched that will be used by the client-side .NET SDK.
- In `LaunchDarkly.Sdk.Internal.Events`, new types `DiagnosticStoreBase` and `DiagnosticConfigProperties` contain logic that was previously only in `LaunchDarkly.ServerSdk` and will now be shared by `LaunchDarkly.ClientSdk`.

### Changed:
- Updated `LaunchDarkly.CommonSdk` to 5.4.0.

## [2.1.1] - 2021-10-05
### Changed:
- Updated dependency versions to keep in sync with dependencies of `LaunchDarkly.ServerSdk`.

## [2.1.0] - 2021-09-21
### Added:
- Made `StateMonitor` and `TaskExecutor` public; they had mistakenly been marked internal.

## [2.0.0] - 2021-09-21
### Added:
- `StateMonitor`, `TaskExecutor`.

### Changed:
- Moved `AtomicBoolean` and `AsyncUtils` into new namespace `LaunchDarkly.Sdk.Internal.Concurrent`.

### Removed:
- `MultiNotifier` (obviated by `StateMonitor`).

## [1.1.2] - 2021-06-07
### Fixed:
- Updated minimum `LaunchDarkly.CommonSdk` version to latest patch, to exclude versions of `LaunchDarkly.JsonStream` with a known parsing bug.

## [1.1.1] - 2021-05-07
### Fixed:
- `HttpProperties.ConnectTimeout` now really sets the TCP connect timeout, if the platform is .NET Core or .NET 5.0. Other platforms don&#39;t support connect timeouts and will continue to ignore this value.

## [1.1.0] - 2021-04-27
### Added:
- `UriExtensions`

### Fixed:
- Improved coverage and reliability of HTTP tests using `LaunchDarkly.TestHelpers`.

## [1.0.0] - 2021-02-08
Initial release of this package, to be used in `LaunchDarkly.ServerSdk` 6.0 and `LaunchDarkly.XamarinSdk` 2.0.
