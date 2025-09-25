# Change log

All notable changes to the LaunchDarkly Client-Side SDK for .NET will be documented in this file.
This project adheres to [Semantic Versioning](http://semver.org).

## [5.5.3](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ClientSdk-v5.5.2...LaunchDarkly.ClientSdk-v5.5.3) (2025-09-25)


### Bug Fixes

* Update EventSource to 5.2.1 ([#159](https://github.com/launchdarkly/dotnet-core/issues/159)) ([35d08d7](https://github.com/launchdarkly/dotnet-core/commit/35d08d7160512850c6d3a81e4fc312696cbb808e))

## [5.5.2](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ClientSdk-v5.5.1...LaunchDarkly.ClientSdk-v5.5.2) (2025-08-26)


### Bug Fixes

* Bump Internal SDK to 3.5.4 and Common SDK to 7.1.0 ([#154](https://github.com/launchdarkly/dotnet-core/issues/154)) ([2746c1a](https://github.com/launchdarkly/dotnet-core/commit/2746c1aa06628cb8af7ff03a65c2b7542195aeff))

## [5.5.1](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ClientSdk-v5.5.0...LaunchDarkly.ClientSdk-v5.5.1) (2025-06-24)


### Bug Fixes

* Bump Internal SDK to 3.5.2 ([#135](https://github.com/launchdarkly/dotnet-core/issues/135)) ([75db503](https://github.com/launchdarkly/dotnet-core/commit/75db503c537f6d06db60747f6357825827efaefe))

## [5.5.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ClientSdk-v5.4.0...LaunchDarkly.ClientSdk-v5.5.0) (2025-06-02)


### Features

* Update SDK client to net8 ([aea1514](https://github.com/launchdarkly/dotnet-core/commit/aea1514b104007d703aafa58adc2ff318f8182b1))
* Update to net8 ([ddae814](https://github.com/launchdarkly/dotnet-core/commit/ddae814250cb21e0de2b953808202addd7098c4c))


### Bug Fixes

* [#104](https://github.com/launchdarkly/dotnet-core/issues/104) dependency optimisation ([#111](https://github.com/launchdarkly/dotnet-core/issues/111)) ([2c7ff62](https://github.com/launchdarkly/dotnet-core/commit/2c7ff629a4c16ad866c6e4ea5b24abc622942f0e))
* Bump Internal SDK version ([#112](https://github.com/launchdarkly/dotnet-core/issues/112)) ([3cb5ae4](https://github.com/launchdarkly/dotnet-core/commit/3cb5ae4e1de89f55d9221997c3ba2c7179ae4a2d))
* Update CommonSDK to v7.0.1 ([#126](https://github.com/launchdarkly/dotnet-core/issues/126)) ([fad338e](https://github.com/launchdarkly/dotnet-core/commit/fad338e651ffe856d6cf5c6e71ba6ff1526d422a))

## [5.4.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ClientSdk-v5.3.0...LaunchDarkly.ClientSdk-v5.4.0) (2025-05-07)


### Features

* Inline context for custom and migration op events in Client SDK ([#96](https://github.com/launchdarkly/dotnet-core/issues/96)) ([6774539](https://github.com/launchdarkly/dotnet-core/commit/6774539af1a2f87b96ae3e647fdcdc5663c791ab))

## [5.3.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ClientSdk-v5.2.1...LaunchDarkly.ClientSdk-v5.3.0) (2024-10-31)


### Features

* client-side prerequisite events ([#24](https://github.com/launchdarkly/dotnet-core/issues/24)) ([f5828cb](https://github.com/launchdarkly/dotnet-core/commit/f5828cb2999cda2ebe5a36c739f54f2b326f4a68))

## [5.2.1](https://github.com/launchdarkly/dotnet-client-sdk/compare/5.2.0...5.2.1) (2024-06-05)


### Bug Fixes

* fixes issue where first flag listener callback was not triggered… ([#97](https://github.com/launchdarkly/dotnet-client-sdk/issues/97)) ([6bf8ec1](https://github.com/launchdarkly/dotnet-client-sdk/commit/6bf8ec160ee29984928bb7320ddd1f6f8580d7a9))

## [5.2.0](https://github.com/launchdarkly/dotnet-client-sdk/compare/5.1.0...5.2.0) (2024-05-08)


### Features

* adds init async with timeout and deprecated non-timeout init functions ([#95](https://github.com/launchdarkly/dotnet-client-sdk/issues/95)) ([41e70f2](https://github.com/launchdarkly/dotnet-client-sdk/commit/41e70f2c49e864da13648bd85c2c427111e502cc))

## [5.1.0](https://github.com/launchdarkly/dotnet-client-sdk/compare/5.0.0...5.1.0) (2024-03-14)


### Features

* Always inline contexts for feature events ([c658bee](https://github.com/launchdarkly/dotnet-client-sdk/commit/c658beee27cd871c8ad91942ac5a04b29b8338bd))
* Redact anonymous attributes within feature events ([c658bee](https://github.com/launchdarkly/dotnet-client-sdk/commit/c658beee27cd871c8ad91942ac5a04b29b8338bd))


### Bug Fixes

* Bump LaunchDarkly.InternalSdk to 3.4.0 ([#91](https://github.com/launchdarkly/dotnet-client-sdk/issues/91)) ([c658bee](https://github.com/launchdarkly/dotnet-client-sdk/commit/c658beee27cd871c8ad91942ac5a04b29b8338bd))

## [5.0.0](https://github.com/launchdarkly/dotnet-client-sdk/compare/4.0.0...5.0.0) (2024-02-13)


### Features

* adds MAUI support ([d01a865](https://github.com/launchdarkly/dotnet-client-sdk/commit/d01a865aa83c6cc699c3d2ff528ce256f169ecdc))
* adds MAUI support ([#66](https://github.com/launchdarkly/dotnet-client-sdk/issues/66)) ([112c2fb](https://github.com/launchdarkly/dotnet-client-sdk/commit/112c2fb7d54c31d88c3a1ffdd9aec88911f149de))


### Bug Fixes

* updating deprecated AndroidClientHandler to AndroidMessageHandler ([973b38c](https://github.com/launchdarkly/dotnet-client-sdk/commit/973b38ccd59a232bf47384b20d8d8bbda6017a6e))
* updating deprecated AndroidClientHandler to AndroidMessageHandler ([#69](https://github.com/launchdarkly/dotnet-client-sdk/issues/69)) ([3dc9dba](https://github.com/launchdarkly/dotnet-client-sdk/commit/3dc9dbaac918555691281322ea15ea94bbe29e5a))


### Miscellaneous Chores

* release 5.0.0 ([#83](https://github.com/launchdarkly/dotnet-client-sdk/issues/83)) ([de859bc](https://github.com/launchdarkly/dotnet-client-sdk/commit/de859bc63555488a6361df3a3e28cdf253df3b45))

## [4.0.0] - 2023-10-18
### Added:
- Added Automatic Mobile Environment Attributes functionality which makes it simpler to target your mobile customers based on application name or version, or on device characteristics including manufacturer, model, operating system, locale, and so on. To learn more, read [Automatic environment attributes](https://docs.launchdarkly.com/sdk/features/environment-attributes).

## [3.1.0] - 2023-10-11
### Added:
- `Configuration.Builder("myKey").ApplicationInfo()`, for configuration of application metadata that may be used in LaunchDarkly analytics or other product features.

## [3.0.2] - 2023-04-04
When using multi-contexts, then this update can change the `FullyQualifiedKey` for a given context. This can cause a cache miss in the local cache for a given context, requiring a connection to LaunchDarkly to populate that cache for the new `FullyQualifiedKey`.

### Fixed:
- Fixed an issue with generating the FullyQualifiedKey. The key generation was not sorted by the kind, so the key was not stable depending on the order of the context construction. This affects how flags are locally cached, as they are cached by the FullyQualifiedKey.

## [3.0.1] - 2023-03-08
### Changed:
- Update to `LaunchDarkly.InternalSdk` `3.1.1`

### Fixed:
- (From LaunchDarkly.InternalSdk) Fixed an issue where calling FlushAndWait with TimeSpan.Zero would never complete if there were no events to flush.

## [3.0.0] - 2022-12-21
The latest version of this SDK supports LaunchDarkly's new custom contexts feature. Contexts are an evolution of a previously-existing concept, "users." Contexts let you create targeting rules for feature flags based on a variety of different information, including attributes pertaining to users, organizations, devices, and more. You can even combine contexts to create "multi-contexts." 

For detailed information about this version, please refer to the list below. For information on how to upgrade from the previous version, please read the [migration guide](https://docs.launchdarkly.com/sdk/client-side/dotnet/migration-2-to-3).

### Added:
- In `LaunchDarkly.Sdk`, the types `Context` and `ContextKind` define the new context model.
- For all SDK methods that took a `User` parameter, there is now a method that takes a `Context`. The corresponding `User` methods are defined as extension methods. The SDK still supports `User` for now, but `Context` is the preferred model and `User` may be removed in a future version.
- `ConfigurationBuilder.GenerateAnonymousKeys` is the new way of enabling the "generate a key for anonymous users" behavior that was previously enabled by setting the user key to null. If you set `GenerateAnonymousKeys` to `true`, all anonymous contexts will have their keys replaced by generated keys; if you do not set it, anonymous contexts will keep whatever placeholder keys you gave them.
- The `TestData` flag builder methods have been extended to support now context-related options, such as matching a key for a specific context type other than "user".
- `LdClient.FlushAndWait()` and `FlushAndWaitAsync()` are equivalent to `Flush()` but will wait for the events to actually be delivered.

### Changed _(breaking changes from 2.x)_:
- It was previously allowable to set a user key to an empty string. In the new context model, the key is not allowed to be empty. Trying to use an empty key will cause evaluations to fail and return the default value.
- There is no longer such a thing as a `Secondary` meta-attribute that affects percentage rollouts. If you set an attribute with that name in a `Context`, it will simply be a custom attribute like any other.
- The `Anonymous` attribute in `User` is now a simple boolean, with no distinction between a false state and a null state.
- Types such as `IPersistentDataStore`, which define the low-level interfaces of LaunchDarkly SDK components and allow implementation of custom components, have been moved out of the `Interfaces` namespace into a new `Subsystems` namespace. Application code normally does not refer to these types except possibly to hold a value for a configuration property such as `ConfigurationBuilder.DataStore`, so this change is likely to only affect configuration-related logic.

### Changed (behavioral changes):
- Analytics event data now uses a new JSON schema due to differences between the context model and the old user model.
- The SDK no longer adds `device` and `os` values to the user attributes. Applications that wish to use device/OS information in feature flag rules must explicitly add such information.

### Changed (requirements/dependencies/build):
- There is no longer a dependency on `LaunchDarkly.JsonStream`. This package existed because some platforms did not support the `System.Text.Json` API, but that is no longer the case and the SDK now uses `System.Text.Json` directly for all of its JSON operations.
- If you are using the package `LaunchDarkly.CommonSdk.JsonNet` for interoperability with the Json.NET library, you must update this to the latest major version.

### Removed:
- Removed all types, fields, and methods that were deprecated as of the most recent 2.x release.
- Removed the `Secondary` meta-attribute in `User` and `UserBuilder`.
- The `Alias` method no longer exists because alias events are not needed in the new context model.
- The `AutoAliasingOptOut` and `InlineUsersInEvents` options no longer exist because they are not relevant in the new context model.
- `LaunchDarkly.Sdk.Json.JsonException`: this type is no longer necessary because the SDK now always uses `System.Text.Json`, so any error when deserializing an object from JSON will throw a `System.Text.Json.JsonException`.

## [2.0.2] - 2022-11-28
### Fixed:
- One of the SDK's dependencies, `LaunchDarkly.Logging`, had an Authenticode signature without a timestamp. The dependency has been updated to a new version with a valid signature. There are no other changes.

## [2.0.1] - 2022-02-08
### Fixed:
- Analytics events generated by `LdClient.Alias` did not have correct timestamps, although this was unlikely to affect how LaunchDarkly processed them.
- The type `LaunchDarkly.Sdk.UnixMillisecondTime` now serializes and deserializes correctly with `System.Text.Json`.

## [2.0.0] - 2022-01-07
This is a major rewrite that introduces a cleaner API design, adds new features, and makes the SDK code easier to maintain and extend. See the [Xamarin 1.x to client-side .NET 2.0 migration guide](https://docs.launchdarkly.com/sdk/client-side/dotnet/migration-1-to-2) for an in-depth look at the changes in 2.0; the following is a summary.

The LaunchDarkly client-side .NET SDK was formerly known as the LaunchDarkly Xamarin SDK. Xamarin for Android and iOS are _among_ its supported platforms, but it can also be used on any platform that supports .NET Core 2+, .NET Standard 2, or .NET 5+. On those platforms, it does not use any Xamarin-specific runtime libraries. To learn more about the distinction between the client-side .NET SDK and the server-side .NET SDK, read: [Client-side and server-side SDKs](https://docs.launchdarkly.com/sdk/concepts/client-side-server-side)

### Added:
- `LdClient.FlagTracker` provides the ability to get notifications when flag values have changed.
- `LdClient.DataSourceStatusProvider` provides information on the status of the SDK's data source (which normally means the streaming connection to the LaunchDarkly service).
- `LdClient.DoubleVariation` and `DoubleVariationDetail` return a numeric flag variation using double-precision floating-point.
- `ConfigurationBuilder.ServiceEndpoints` allows you to override the regular service URIs— as you may want to do if you are using the LaunchDarkly Relay Proxy, for instance— in a single place. Previously, the URIs had to be specified individually for each service (`StreamingDataSource().BaseURI`, `SendEvents().BaseURI`, etc.).
- `HttpConfigurationBuilder.UseReport` tells the SDK to make HTTP `REPORT` requests rather than `GET` requests to the LaunchDarkly service endpoints, which may be desirable in rare circumstances but is not available on all platforms.
- `ConfigurationBuilder.Persistence` and `PersistenceConfigurationBuilder.MaxCachedUsers` allow setting a limit on how many users' flag data can be saved in persistent local storage, or turning off persistence.
- The `LaunchDarkly.Sdk.Json` namespace provides methods for converting types like `User` and `FeatureFlagsState` to and from JSON.
- The `LaunchDarkly.Sdk.UserAttribute` type provides a less error-prone way to refer to user attribute names in configuration, and can also be used to get an arbitrary attribute from a user.
- The `LaunchDarkly.Sdk.UnixMillisecondTime` type provides convenience methods for converting to and from the Unix epoch millisecond time format that LaunchDarkly uses for all timestamp values.
- The SDK now periodically sends diagnostic data to LaunchDarkly, describing the version and configuration of the SDK, the architecture and version of the runtime platform, and performance statistics. No credentials, hostnames, or other identifiable values are included. This behavior can be disabled with `ConfigurationBuilder.DiagnosticOptOut` or configured with `ConfigurationBuilder.DiagnosticRecordingInterval`.

### Changed (requirements/dependencies/build):
- .NET Standard 1.6 is no longer supported.
- The SDK no longer has a dependency on `Common.Logging`. Instead, it uses a similar but simpler logging facade, the [`LaunchDarkly.Logging`](https://github.com/launchdarkly/dotnet-logging) package, which has adapters for various logging destinations.
- The SDK no longer has a dependency on the Json.NET library (a.k.a. `Newtonsoft.Json`), but instead uses a lightweight custom JSON serializer and deserializer. This removes the potential for dependency version conflicts in applications that use Json.NET for their own purposes, and reduces the number of dependencies in applications that do not use Json.NET. If you do use Json.NET and you want to use it with SDK data types like `User` and `LdValue`, see [`LaunchDarkly.CommonSdk.JsonNet`](https://github.com/launchdarkly/dotnet-sdk-common/tree/master/src/LaunchDarkly.CommonSdk.JsonNet). Those types also serialize/deserialize correctly with the `System.Text.Json` API on platforms where that API is available.
 
### Changed (API changes):
- The base namespace has changed: types that were previously in `LaunchDarkly.Client` are now in `LaunchDarkly.Sdk`, and types that were previously in `LaunchDarkly.Xamarin` are now in `LaunchDarkly.Sdk.Client`. The `LaunchDarkly.Sdk` namespace contains types that are not specific to the _client-side_ .NET SDK (that is, they are also used by the server-side .NET SDK): `EvaluationDetail`, `LdValue`, `User`, and `UserBuilder`. Types that are specific to the client-side .NET SDK, such as `Configuration` and `LdClient`, are in `LaunchDarkly.Sdk.Client`.
- `User` and `Configuration` objects are now immutable. To specify properties for these classes, you must now use `User.Builder` and `Configuration.Builder`.
- `Configuration.Builder` now returns a concrete type rather than an interface.
- `EvaluationDetail` is now a struct type rather than a class.
- `EvaluationReason` is now a single struct type rather than a base class with subclasses.
- `EvaluationReasonKind` and `EvaluationErrorKind` constants now use .Net-style naming (`RuleMatch`) rather than Java-style naming (`RULE_MATCH`). Their JSON representations are unchanged.
- The `ILdClient` interface is now in `LaunchDarkly.Sdk.Client.Interfaces` instead of the main namespace.
- The `ILdClientExtensions` methods `EnumVariation<T>` and `EnumVariationDetail<T>` now have type constraints to enforce that `T` really is an `enum` type.

### Changed (behavioral changes):
- The default event flush interval is now 30 seconds on mobile platforms, instead of 5 seconds. This is consistent with the other mobile SDKs and is intended to reduce network traffic.
- Logging now uses a simpler, more stable set of logger names instead of using the names of specific implementation classes that are subject to change. General messages are logged under `LaunchDarkly.Sdk`, while messages about specific areas of functionality are logged under that name plus `.DataSource` (streaming, polling, file data, etc.), `.DataStore` (database integrations), `.Evaluation` (unexpected errors during flag evaluations), or `.Events` (analytics event processing).

### Removed:
- All types and methods that were deprecated as of the last 1.x release have been removed.

## [2.0.0-rc.1] - 2021-11-19
This is the first release candidate version of the LaunchDarkly client-side .NET SDK 2.0-- a major rewrite that introduces a cleaner API design, adds new features, and makes the SDK code easier to maintain and extend. See the [Xamarin 1.x to client-side .NET 2.0 migration guide](https://docs.launchdarkly.com/sdk/client-side/dotnet/migration-1-to-2) for an in-depth look at the changes in 2.0; the following is a summary.

The LaunchDarkly client-side .NET SDK was formerly known as the LaunchDarkly Xamarin SDK. Xamarin for Android and iOS are _among_ its supported platforms, but it can also be used on any platform that supports .NET Core 2+, .NET Standard 2, or .NET 5+. On those platforms, it does not use any Xamarin-specific runtime libraries. To learn more about the distinction between the client-side .NET SDK and the server-side .NET SDK, read: [Client-side and server-side SDKs](https://docs.launchdarkly.com/sdk/concepts/client-side-server-side)

### Added:
- `LdClient.FlagTracker` provides the ability to get notifications when flag values have changed.
- `LdClient.DataSourceStatusProvider` provides information on the status of the SDK's data source (which normally means the streaming connection to the LaunchDarkly service).
- `LdClient.DoubleVariation` and `DoubleVariationDetail` return a numeric flag variation using double-precision floating-point.
- `HttpConfigurationBuilder.UseReport` tells the SDK to make HTTP `REPORT` requests rather than `GET` requests to the LaunchDarkly service endpoints, which may be desirable in rare circumstances but is not available on all platforms.
- `ConfigurationBuilder.Persistence` and `PersistenceConfigurationBuilder.MaxCachedUsers` allow setting a limit on how many users' flag data can be saved in persistent local storage, or turning off persistence.
- The `LaunchDarkly.Sdk.Json` namespace provides methods for converting types like `User` and `FeatureFlagsState` to and from JSON.
- The `LaunchDarkly.Sdk.UserAttribute` type provides a less error-prone way to refer to user attribute names in configuration, and can also be used to get an arbitrary attribute from a user.
- The `LaunchDarkly.Sdk.UnixMillisecondTime` type provides convenience methods for converting to and from the Unix epoch millisecond time format that LaunchDarkly uses for all timestamp values.
- The SDK now periodically sends diagnostic data to LaunchDarkly, describing the version and configuration of the SDK, the architecture and version of the runtime platform, and performance statistics. No credentials, hostnames, or other identifiable values are included. This behavior can be disabled with `ConfigurationBuilder.DiagnosticOptOut` or configured with `ConfigurationBuilder.DiagnosticRecordingInterval`.

### Changed (requirements/dependencies/build):
- .NET Standard 1.6 is no longer supported.
- The SDK no longer has a dependency on `Common.Logging`. Instead, it uses a similar but simpler logging facade, the [`LaunchDarkly.Logging`](https://github.com/launchdarkly/dotnet-logging) package, which has adapters for various logging destinations.
- The SDK no longer has a dependency on the Json.NET library (a.k.a. `Newtonsoft.Json`), but instead uses a lightweight custom JSON serializer and deserializer. This removes the potential for dependency version conflicts in applications that use Json.NET for their own purposes, and reduces the number of dependencies in applications that do not use Json.NET. If you do use Json.NET and you want to use it with SDK data types like `User` and `LdValue`, see [`LaunchDarkly.CommonSdk.JsonNet`](https://github.com/launchdarkly/dotnet-sdk-common/tree/master/src/LaunchDarkly.CommonSdk.JsonNet). Those types also serialize/deserialize correctly with the `System.Text.Json` API on platforms where that API is available.
 
### Changed (API changes):
- The base namespace has changed: types that were previously in `LaunchDarkly.Client` are now in `LaunchDarkly.Sdk`, and types that were previously in `LaunchDarkly.Xamarin` are now in `LaunchDarkly.Sdk.Client`. The `LaunchDarkly.Sdk` namespace contains types that are not specific to the _client-side_ .NET SDK (that is, they are also used by the server-side .NET SDK): `EvaluationDetail`, `LdValue`, `User`, and `UserBuilder`. Types that are specific to the client-side .NET SDK, such as `Configuration` and `LdClient`, are in `LaunchDarkly.Sdk.Client`.
- `User` and `Configuration` objects are now immutable. To specify properties for these classes, you must now use `User.Builder` and `Configuration.Builder`.
- `Configuration.Builder` now returns a concrete type rather than an interface.
- `EvaluationDetail` is now a struct type rather than a class.
- `EvaluationReason` is now a single struct type rather than a base class with subclasses.
- `EvaluationReasonKind` and `EvaluationErrorKind` constants now use .Net-style naming (`RuleMatch`) rather than Java-style naming (`RULE_MATCH`). Their JSON representations are unchanged.
- The `ILdClient` interface is now in `LaunchDarkly.Sdk.Client.Interfaces` instead of the main namespace.
- The `ILdClientExtensions` methods `EnumVariation<T>` and `EnumVariationDetail<T>` now have type constraints to enforce that `T` really is an `enum` type.

### Changed (behavioral changes):
- The default event flush interval is now 30 seconds on mobile platforms, instead of 5 seconds. This is consistent with the other mobile SDKs and is intended to reduce network traffic.
- Logging now uses a simpler, more stable set of logger names instead of using the names of specific implementation classes that are subject to change. General messages are logged under `LaunchDarkly.Sdk.Xamarin.LdClient`, while messages about specific areas of functionality are logged under that name plus `.DataSource` (streaming, polling, file data, etc.), `.DataStore` (database integrations), `.Evaluation` (unexpected errors during flag evaluations), or `.Events` (analytics event processing).
 
### Fixed:
- The SDK was deciding whether to send analytics events based on the `Offline` property of the _original_ SDK configuration, rather than whether the SDK is _currently_ in offline mode or not.
 
### Removed:
- All types and methods that were shown as deprecated/`Obsolete` in the last 1.x release have been removed.

## [1.2.2] - 2021-04-06
### Fixed:
- The SDK was failing to get flags in streaming mode when connecting to a LaunchDarkly Relay Proxy instance.

## [1.2.1] - 2021-03-30
### Fixed:
- Removed unnecessary dependencies on `Xamarin.Android.Support.Core.Utils` and `Xamarin.Android.Support.CustomTabs`. (Thanks, [Vladimir-Mischenchuk](https://github.com/launchdarkly/xamarin-client-sdk/pull/25)!)
- Setting custom base URIs now works correctly even if the base URI includes a path prefix (such as you might use with a reverse proxy that rewrites request URIs). ([#26](https://github.com/launchdarkly/xamarin-client-sdk/issues/26))
- Fixed the base64 encoding of user properties in request URIs to use the URL-safe variant of base64.

## [1.2.0] - 2020-01-15
### Added:
- Added `ILdClient` extension methods `EnumVariation` and `EnumVariationDetail`, which convert strings to enums.
- `User.Secondary`, `IUserBuilder.Secondary` (replaces `SecondaryKey`).
- `EvaluationReason` static methods and properties for creating reason instances.
- `LdValue` helpers for dealing with array/object values, without having to use an intermediate `List` or `Dictionary`: `BuildArray`, `BuildObject`, `Count`, `Get`.
- `LdValue.Parse()`.
 
### Changed:
- `EvaluationReason` properties all exist on the base class now, so for instance you do not need to cast to `RuleMatch` to get the `RuleId` property. This is in preparation for a future API change in which `EvaluationReason` will become a struct instead of a base class.
 
### Fixed:
- Calling `Identify` or `IdentifyAsync` with a user that has a null key and `Anonymous(true)`-- which should generate a unique key for the anonymous user-- did not work. The symptom was that the client would fail to retrieve the flags, and the call would either never complete (for `IdentifyAsync`) or time out (for `Identify`). This has been fixed.
- Improved memory usage and performance when processing analytics events: the SDK now encodes event data to JSON directly, instead of creating intermediate objects and serializing them via reflection.
- When parsing arbitrary JSON values, the SDK now always stores them internally as `LdValue` rather than `JToken`. This means that no additional copying step is required when the application accesses that value, if it is of a complex type.
- `LdValue.Equals()` incorrectly returned true for object (dictionary) values that were not equal.
- The SDK now specifies a uniquely identifiable request header when sending events to LaunchDarkly to ensure that events are only processed once, even if the SDK sends them two times due to a failed initial attempt.
 
### Deprecated:
- `IUserBuilder.SecondaryKey`, `User.SecondaryKey`.
- `EvaluationReason` subclasses. Use only the base class properties and methods to ensure compatibility with future versions.

## [1.1.1] - 2019-10-23
### Fixed:
- The JSON serialization of `User` was producing an extra `Anonymous` property in addition to `anonymous`. If Newtonsoft.Json was configured globally to force all properties to lowercase, this would cause an exception when serializing a user since the two properties would end up with the same name. ([#22](https://github.com/launchdarkly/xamarin-client-sdk/issues/22))

## [1.1.0] - 2019-10-17
### Added:
- Added support for upcoming LaunchDarkly experimentation features. See `ILdClient.Track(string, LdValue, double)`.
- `User.AnonymousOptional` and `IUserBuilder.AnonymousOptional` allow treating the `Anonymous` property as nullable (necessary for consistency with other SDKs). See note about this under Fixed.
- Added `LaunchDarkly.Logging.ConsoleAdapter` as a convenience for quickly enabling console logging; this is equivalent to `Common.Logging.Simple.ConsoleOutLoggerFactoryAdapter`, but the latter is not available on some platforms.
 
### Fixed:
- `Configuration.Builder` was not setting a default value for the `BackgroundPollingInterval` property. As a result, if you did not set the property explicitly, the SDK would throw an error when the application went into the background on mobile platforms.
- `IUserBuilder` was incorrectly setting the user's `Anonymous` property to `null` even if it had been explicitly set to `false`. Null and false behave the same in terms of LaunchDarkly's user indexing behavior, but currently it is possible to create a feature flag rule that treats them differently. So `IUserBuilder.Anonymous(false)` now correctly sets it to `false`, just as the deprecated method `UserExtensions.WithAnonymous(false)` would.
- `LdValue.Convert.Long` was mistakenly converting to an `int` rather than a `long`. (CommonSdk [#32](https://github.com/launchdarkly/dotnet-sdk-common/issues/32))

## [1.0.0] - 2019-09-13

First GA release.

For release notes on earlier beta versions, see the [beta changelog](https://github.com/launchdarkly/xamarin-client-sdk/blob/1.0.0-beta24/CHANGELOG.md).
