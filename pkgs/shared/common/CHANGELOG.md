# Change log

All notable changes to `LaunchDarkly.CommonSdk` will be documented in this file. For full release notes for the projects that depend on this project, see their respective changelogs. This file describes changes only to the common code. This project adheres to [Semantic Versioning](http://semver.org).

## [7.0.1](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.CommonSdk-v7.0.0...LaunchDarkly.CommonSdk-v7.0.1) (2025-06-02)


### Bug Fixes

* [#104](https://github.com/launchdarkly/dotnet-core/issues/104) dependency optimisation ([#111](https://github.com/launchdarkly/dotnet-core/issues/111)) ([2c7ff62](https://github.com/launchdarkly/dotnet-core/commit/2c7ff629a4c16ad866c6e4ea5b24abc622942f0e))
* Bump System.Text.Json from 6.0.0 to 6.0.10 ([#110](https://github.com/launchdarkly/dotnet-core/issues/110)) ([3673796](https://github.com/launchdarkly/dotnet-core/commit/36737961880c807b8f9d5bd6cc7313efb4b3ad26))

## [7.0.0] - 2023-10-17
### Changed:
- IEnvironmentReporter now reports nullable values.

## [6.2.0] - 2023-10-10
### Added:
- Adds locale to auto environment attribute layer.

## [6.1.0] - 2023-10-10
### Added:
- Adds ApplicationInfo and EnvironmentReporter and respective builders.

## [6.0.1] - 2023-04-04
### Fixed:
- Fixed an issue with generating the `FullyQualifiedKey`. The key generation was not sorted by the kind, so the key was not stable depending on the order of the context construction. This also affected the generation of the secure mode hash for mulit-contexts.

## [6.0.0] - 2022-12-01
This major version release of `LaunchDarkly.CommonSdk` corresponds to the upcoming v7.0.0 release of the LaunchDarkly server-side .NET SDK (`LaunchDarkly.ServerSdk`) and the v3.0.0 release of the LaunchDarkly client-side .NET SDK (`LaunchDarkly.ClientSdk`), and cannot be used with earlier SDK versions.

### Added:
- In `LaunchDarkly.Sdk`, the types `Context` and `ContextKind` define the new "context" model. "Contexts" are a replacement for the earlier concept of "users"; they can be populated with attributes in more or less the same way as before, but they also support new behaviors. More information about these features will be included in the release notes for the `LaunchDarkly.ServerSdk` 7.0.0 and `LaunchDarkly.ClientSdk` 3.0.0 releases.

### Changed:
- .NET Core 2.1, .NET Framework 4.5.2, .NET Framework 4.6.1, and .NET 5.0 are now unsupported. The minimum platform versions are now .NET Core 3.1, .NET Framework 4.6.2, .NET 6.0, and .NET Standard 2.0.
- It was previously allowable to set a user key to an empty string. In the new context model, the key is not allowed to be empty. Trying to use an empty key will cause evaluations to fail and return the default value.
- There is no longer such a thing as a `Secondary` meta-attribute that affects percentage rollouts. If you set an attribute with that name in a `Context`, it will simply be a custom attribute like any other.
- The `Anonymous` attribute in `LDUser` is now a simple boolean, with no distinction between a false state and a null state.
- There is no longer a dependency on `LaunchDarkly.JsonStream`. This package existed because some platforms did not support the `System.Text.Json` API, but that is no longer the case and the SDK now uses `System.Text.Json` directly for all of its JSON operations.
- If you are using the package `LaunchDarkly.CommonSdk.JsonNet` for interoperability with the Json.NET library, you must update this to the latest major version.

### Removed:
- Removed all types, fields, and methods that were deprecated as of the most recent release.
- Removed the `Secondary` meta-attribute in `User` and `UserBuilder`.

## [5.5.0] - 2022-02-02
### Added:
- `UnixMillisecondTime` now has a JSON converter like other `LaunchDarkly.Sdk` types.

### Fixed:
- When using `LaunchDarkly.CommonSdk.JsonNet`, nullable value types such as `EvaluationReason?` were not being serialized correctly.

## [5.4.1] - 2021-11-02
### Fixed:
- Copying a user with `User.Builder(existingUser)` was incorrectly changing the default `null` value of `AnonymousOptional` to `false`. This normally has no significance since LaunchDarkly treats those two values the same, but it could have broken tests that expected a copied user to be equal.

## [5.4.0] - 2021-10-22
### Added:
- `LdValue.ObjectBuilder.Remove`.
- User builder `Custom` overloads for `long` and `double`.

### Changed:
- Added more doc comment text about numeric precision issues with JSON numbers.
- Updated `LaunchDarkly.JsonStream` to 1.0.3.

## [5.3.0] - 2021-10-14
### Added:
- Convenience methods for working with JSON object and array values: `LdValue.Dictionary`, `LdValue.List`, `LdValue.ObjectBuilder.Set`, and `LdValue.ObjectBuilder.Copy`.

## [5.2.1] - 2021-10-05
### Changed:
- Changed dependency version for `System.Collections.Immutable` to 1.7.1, to match the version used by `LaunchDarkly.ServerSdk`. This has no effect on SDK functionality, but it reduces the chance that a binding redirect will be required to reconcile dependency versions in .NET Framework.

## [5.2.0] - 2021-07-19
### Added:
- In `EvaluationReason`, added optional status information related to the new big segments feature.

## [5.1.0] - 2021-06-17
### Added:
- The SDK now supports the ability to control the proportion of traffic allocation to an experiment. This works in conjunction with a new platform feature now available to early access customers.

## [5.0.2] - 2021-06-07
### Fixed:
- Updated the minimum dependency version for `LaunchDarkly.JsonStream` to exclude versions that have a known JSON parsing bug.

## [5.0.1] - 2021-02-02
### Fixed:
- Updated dependencies in `LaunchDarkly.CommonSdk.JsonNet` to the correct versions.

## [5.0.0] - 2021-02-02
### Added:
- `LaunchDarkly.Sdk.Json` namespace with JSON serialization helpers. Also, there is now a separate package defined in this repo, `LaunchDarkly.CommonSdk.JsonNet`, for interoperability with `Newtonsoft.Json`.
- `UnixMillisecondTime` type, a convenient wrapper for the date/time format that is used by LaunchDarkly services. Applications normally won&#39;t need to use this unless they are interacting directly with the analytics event system.
- `LdValue` now has `==` and `!=` operators.
- Releases now publish [Source Link](https://github.com/dotnet/sourcelink/blob/master/README.md) data.

### Changed:
- The base namespace is now `LaunchDarkly.Sdk` rather than `LaunchDarkly.Client`.
- `EvaluationReason` is now a struct.
- `EvaluationReasonKind` and `EvaluationErrorKind` enum names now use regular .NET-style capitalization (`RuleMatch`) instead of Java-style capitalization (`RULE_MATCH`).
- JSON-serializable types (`User`, etc.) now automatically encode and decode correctly with `System.Text.Json`.

### Removed:
- `EvaluationReason` subclasses.
- There is no longer a package dependency on `Newtonsoft.Json`.
- Non-public helpers used by SDKs have been removed, and are now in `LaunchDarkly.InternalSdk` instead.

## [4.3.1] - 2020-01-15
### Fixed:
- A bug in the SDK prevented the sending of events from being retried after a failure. The SDK now retries once after an event flush fails as was intended.
- The SDK now specifies a uniquely identifiable request header when sending events to LaunchDarkly to ensure that events are only processed once, even if the SDK sends them two times due to a failed initial attempt.

## [4.3.0] - 2020-01-13
### Added:
- `EvaluationReason` static methods and properties for creating reason instances.
- `LdValue` helpers for dealing with array/object values, without having to use an intermediate `List` or `Dictionary`: `BuildArray`, `BuildObject`, `Count`, `Get`.
- `LdValue.Parse()`.
- `IUserBuilder.Secondary` is a new name for `SecondaryKey` (for consistency with other SDKs), and allows you to make the `secondary` attribute private.
- `User.Secondary` (same as `SecondaryKey`).

### Changed:
- `EvaluationReason` properties all exist on the base class now, so for instance you do not need to cast to `RuleMatch` to get the `RuleId` property. This is in preparation for a future API change in which `EvaluationReason` will become a struct instead of a base class.

### Fixed:
- Improved memory usage and performance when processing analytics events: the SDK now encodes event data to JSON directly, instead of creating intermediate objects and serializing them via reflection.
- When parsing arbitrary JSON values, the SDK now always stores them internally as `LdValue` rather than `JToken`. This means that no additional copying step is required when the application accesses that value, if it is of a complex type.
- `LdValue.Equals()` incorrectly returned true for object (dictionary) values that were not equal.

### Deprecated:
- `EvaluationReason` subclasses. Use only the base class properties and methods to ensure compatibility with future versions.
- `IUserBuilder.SecondaryKey`, `User.SecondaryKey`.


## [4.2.1] - 2019-10-23
### Fixed:
- The JSON serialization of `User` was producing an extra `Anonymous` property in addition to `anonymous`. If Newtonsoft.Json was configured globally to force all properties to lowercase, this would cause an exception when serializing a user since the two properties would end up with the same name.

## [4.2.0] - 2019-10-10
### Added:
- Added `LaunchDarkly.Logging.ConsoleAdapter` as a convenience for quickly enabling console logging; this is equivalent to `Common.Logging.Simple.ConsoleOutLoggerFactoryAdapter`, but the latter is not available on some platforms.

## [4.1.0] - 2019-10-07
### Added:
- `IUserBuilder.AnonymousOptional` and `User.AnonymousOption` allow treating the `Anonymous` property as nullable (necessary for consistency with other SDKs). See note about this under Fixed.
 
### Fixed:
- `IUserBuilder` was incorrectly setting the user's `Anonymous` property to `null` even if it had been explicitly set to `false`. Null and false behave the same in terms of LaunchDarkly's user indexing behavior, but currently it is possible to create a feature flag rule that treats them differently. So `IUserBuilder.Anonymous(false)` now correctly sets it to `false`.
- `LdValue.Convert.Long` was mistakenly converting to an `int` rather than a `long`. ([#32](https://github.com/launchdarkly/dotnet-sdk-common/issues/32))

## [4.0.1] - 2019-09-13
_The 4.0.0 release was broken._

### Added:
- `LdValue` now has methods for converting to and from complex types (list, dictionary).

### Changed:
- `ImmutableJsonValue` is now called `LdValue`.
- All public APIs now use `ImmutableJsonValue` instead of `JToken`.
 
### Removed:
- Public `ImmutableJsonValue` methods and properties that refer to `JToken`, `JObject`, or `JArray`.

## [3.1.0] - 2019-08-30
### Added:
- `SetOffline` method in `IEventProcessor`/`DefaultEventProcessor`.
- XML documentation comments are now included in the package for all target frameworks. Previously they were only included for .NET Standard 1.4.

## [3.0.0] - 2019-08-09
### Added:
- `User.Builder` provides a fluent builder pattern for constructing `User` objects. This is now the only method for building a user if you want to set any properties other than the `Key`.
- The `ImmutableJsonValue` type provides a wrapper for the Newtonsoft.Json types that prevents accidentally modifying JSON object properties or array values that are shared by other objects.
- Helper type `ValueType`/`ValueTypes` for use by the SDK `Variation` methods.
- Internal interfaces for configuring specific components, like `IEventProcessorConfiguration`. These replace `IBaseConfiguration`.

### Changed:
- `User` objects are now immutable.
- In `User`, `IpAddress` has been renamed to `IPAddress` (standard .NET capitalization for two-letter acronyms).
- Custom attributes in `User.Custom` now use the type `ImmutableJsonValue` instead of `JToken`.
- Uses of mutable `IDictionary` and `ISet` in the configuration and user objects have been changed to immutable types.

### Removed:
- `UserExtensions` (use `User.Builder`).
- `User` constructors (use `User.WithKey` or `User.Builder`).
- `User` property setters.
- `IBaseConfiguration` and `ICommonLdClient` interfaces.

### Fixed:
- No longer assumes that we are overriding the `HttpMessageHandler` (if it is null in the configuration, just use the default `HttpClient` constructor). This is important for Xamarin.

## [2.11.1] - 2020-11-05
### Changed:
- Updated the `LaunchDarkly.EventSource` dependency to a version that has a specific target for .NET Standard 2.0. Previously, that package targeted only .NET Standard 1.4 and .NET Framework 4.5. There is no functional difference between these targets, but .NET Core application developers may wish to avoid linking to any .NET Standard 1.x assemblies on general principle.

## [2.11.0] - 2020-01-31
### Added:
- `DefaultEventProcessor` now supports sending diagnostic data to LaunchDarkly regarding the OS version, performance statistics, etc. The exact implementation of this is determined by the platform-specific SDKs (.NET or Xamarin).
- The SDK now specifies a uniquely identifiable request header when sending events to LaunchDarkly to ensure that events are only processed once, even if the SDK sends them two times due to a failed initial attempt.

## [2.10.1] - 2020-01-15
### Fixed:
- A bug in the SDK prevented the sending of events from being retried after a failure. The SDK now retries once after an event flush fails as was intended.
- The SDK now specifies a uniquely identifiable request header when sending events to LaunchDarkly to ensure that events are only processed once, even if the SDK sends them two times due to a failed initial attempt.

## [2.10.0] - 2020-01-03
### Added:
- `IUserBuilder.Secondary` is a new name for `SecondaryKey` (for consistency with other SDKs), and allows you to make the `secondary` attribute private.
- `User.Secondary` (same as `SecondaryKey`).

### Deprecated:
- `IUserBuilder.SecondaryKey`, `User.SecondaryKey`.


## [2.9.2] - 2019-11-12
### Fixed:
- `LdValue.Equals()` incorrectly returned true for object (dictionary) values that were not equal.
- Summary events incorrectly had `unknown:true` for all evaluation errors, rather than just for "flag not found" errors (bug introduced in 2.9.0, not used in any current SDK).

## [2.9.1] - 2019-11-08
### Fixed:
- Fixed an exception when serializing user custom attributes in events (bug in 2.9.0).

## [2.9.0] - 2019-11-08
### Added:
- `EvaluationReason` static methods and properties for creating reason instances.
- `LdValue` helpers for dealing with array/object values, without having to use an intermediate `List` or `Dictionary`: `BuildArray`, `BuildObject`, `Count`, `Get`.
- `LdValue.Parse()`. It is also possible to use `Newtonsoft.Json.JsonConvert` to parse or serialize `LdValue`, but since the implementation may change in the future, using the type's own methods is preferable.

### Changed:
- `EvaluationReason` properties all exist on the base class now, so for instance you do not need to cast to `RuleMatch` to get the `RuleId` property. This is in preparation for a future API change in which `EvaluationReason` will become a struct instead of a base class.

### Fixed:
- Improved memory usage and performance when processing analytics events: the SDK now encodes event data to JSON directly, instead of creating intermediate objects and serializing them via reflection.

### Deprecated:
- `EvaluationReason` subclasses. Use only the base class properties and methods to ensure compatibility with future versions.

## [2.8.0] - 2019-10-10
### Added:
- Added `LaunchDarkly.Logging.ConsoleAdapter` as a convenience for quickly enabling console logging; this is equivalent to `Common.Logging.Simple.ConsoleOutLoggerFactoryAdapter`, but the latter is not available on some platforms.

## [2.7.0] - 2019-10-03
### Added:
- `IUserBuilder.AnonymousOptional` allows setting the `Anonymous` property to `null` (necessary for consistency with other SDKs). See note about this under Fixed.

### Fixed:
- `IUserBuilder` was incorrectly setting the user's `Anonymous` property to `null` even if it had been explicitly set to `false`. Null and false behave the same in terms of LaunchDarkly's user indexing behavior, but currently it is possible to create a feature flag rule that treats them differently. So `IUserBuilder.Anonymous(false)` now correctly sets it to `false`, just as the deprecated method `UserExtensions.WithAnonymous(false)` would.
- `LdValue.Convert.Long` was mistakenly converting to an `int` rather than a `long`. ([#32](https://github.com/launchdarkly/dotnet-sdk-common/issues/32))

## [2.6.1] - 2019-09-12
### Fixed:
- A packaging error made the `LaunchDarkly.CommonSdk.StrongName` package unusable in 2.6.0.

## [2.6.0] - 2019-09-12
### Added:
- Value type `LdValue`, to be used in place of `JToken` whenever possible.

### Changed:
- All event-related code except for public properties now uses `LdValue`.

### Removed:
- Internal helper type `ValueType`, unnecessary now because we can use `LdValue.Convert`.

## [2.5.1] - 2019-08-30
### Fixed:
- Many improvements to XML documentation comments.

## [2.5.0] - 2019-08-30
### Added:
- Internal helper types `ValueType` and `ValueTypes`.
- XML documentation comments are now included in the package for all target frameworks. Previously they were only included for .NET Standard 1.4.

### Changed:
- Internal types are now sealed.
- Changed some internal classes to structs for efficiency.

### Deprecated:
- `IBaseConfiguration` and `ICommonLdClient` interfaces.

## [2.4.0] - 2019-07-31
### Added:
- `IBaseConfiguration.EventCapacity` and `IBaseConfiguration.EventFlushInterval`.
- `UserBuilder.Key` setter.

### Deprecated:
- `IBaseConfiguration.SamplingInterval`.
- `IBaseConfiguration.EventQueueCapacity` (now a synonym for `EventCapacity`).
- `IBaseConfiguration.EventQueueFrequency` (now a synonym for `EventFlushInterval`).

## [2.3.0] - 2019-07-23
### Deprecated:
- `User` constructors.
- `User.Custom` and `User.PrivateAttributeNames` will be changed to immutable collections in the future.

## [2.2.0] - 2019-07-23
### Added:
- `User.Builder` provides a fluent builder pattern for constructing `User` objects. This is now the preferred method for building a user, rather than setting `User` properties directly or using `UserExtension` methods like `AndName()` that modify the existing user object.
- `User.IPAddress` is equivalent to `User.IpAddress`, but has the standard .NET capitalization for two-letter acronyms.

### Deprecated:
- `User.IpAddress` (use `IPAddress`).
- All `UserExtension` methods are now deprecated. The setters for all `User` properties should also be considered deprecated, although C# does not allow these to be marked with `[Obsolete]`.

## [2.1.2] - 2019-05-10
### Fixed:
- Fixed a build error that caused classes to be omitted from `LaunchDarkly.CommonSdk.StrongName`.

## [2.1.1] - 2019-05-10
### Changed:
- The package and assembly name are now `LaunchDarkly.CommonSdk`, and the `InternalsVisibleTo` directives now refer to `LaunchDarkly.ServerSdk` and `LaunchDarkly.XamarinSdk`. There are no other changes. All future releases of the LaunchDarkly server-side .NET SDK and client-side Xamarin SDK will use the new package names, and no further updates of the old `LaunchDarkly.Common` package will be published.

## [2.1.0] - 2019-04-16
### Added:
- Added support for planned future LaunchDarkly features related to analytics events and experimentation (metric values).

## [2.0.0] - 2019-03-26
### Added:
- Added support for planned future LaunchDarkly features related to analytics events and experimentation.
- It is now possible to deserialize evaluation reasons from JSON (this is used by the Xamarin client).

### Changed:
- The `IFlagEventProperties` interface was extended and modified to support the aforementioned features.

### Fixed:
- Under some circumstances, a `CancellationTokenSource` might not be disposed of after making an HTTP request, which could cause a timer object to be leaked.

## [1.2.3] - 2018-01-14
### Fixed:
- The assemblies in this package now have Authenticode signatures.

## [1.2.2] - 2018-01-09

This release was an error. It works, but there are no changes from 1.2.1 except for using a newer version of `dotnet-eventsource`, which was also an unintended re-release of the previous version.

## [1.2.1] - 2018-12-17

### Changed
The only changes in this version are to the build:

- What is published to NuGet is now the Release configuration, without debug information.
- The Debug configuration (the default) no longer performs strong-name signing. This makes local development easier.
- `LaunchDarkly.Common` now has an `InternalsVisibleTo` directive for an _unsigned_ version of the `LaunchDarkly.Client` unit tests. Again this is to support local development, since the client will be unsigned by default as well.

## [1.2.0] - 2018-10-24

### Changed
- The non-strong-named version of this library (`LaunchDarkly.Common`) can now be used with a non-strong-named version of `LaunchDarkly.Client`, which does not normally exist but could be built as part of a fork of the SDK.

- Previously, the delay before stream reconnect attempts would increase exponentially only if the previous connection could not be made at all or returned an HTTP error; if it received an HTTP 200 status, the delay would be reset to the minimum even if the connection then immediately failed. Now, if the stream connection fails after it has been up for less than a minute, the reconnect delay will continue to increase. (changed in `LaunchDarkly.EventSource` 3.2.0)

### Fixed

- Fixed an [unobserved exception](https://blogs.msdn.microsoft.com/pfxteam/2011/09/28/task-exception-handling-in-net-4-5/) that could occur following a stream timeout, which could cause a crash in .NET 4.0. (fixed in `LaunchDarkly.EventSource` 3.2.0)

- A `NullReferenceException` could sometimes be logged if a stream connection failed. (fixed in `LaunchDarkly.EventSource` 3.2.0)

## [1.1.1] - 2018-08-29

Incorporates the fix from 1.0.6 that was not included in 1.1.0.

## [1.1.0] - 2018-08-22

### Added
- New `EvaluationDetail` and `EvaluationReason` classes will be used in future SDK versions that support capturing evaluation reasons.

## [1.0.6] - 2018-08-30

### Fixed
- Updated LaunchDarkly.EventSource to fix a bug that prevented the client from reconnecting to the stream if it received an HTTP error status from the server (as opposed to simply losing the connection).

## [1.0.5] - 2018-08-14

### Fixed
- The reconnection attempt counter is no longer shared among all StreamManager instances. Previously, if you connected to more than one stream, all but the first would behave as if they were reconnecting and would have a backoff delay.

## [1.0.4] - 2018-08-02

### Changed
- Updated the dependency on `LaunchDarkly.EventSource`, which no longer has package references to System assemblies.

## [1.0.3] - 2018-07-27

### Changed
- The package `LaunchDarkly.Common` is no longer strong-named. Instead, we are now building two packages: `LaunchDarkly.Common` and `LaunchDarkly.Common.StrongName`. This is because the Xamarin project requires an unsigned version of the package, whereas the main .NET SDK uses the signed one.
- The project now uses a framework reference (`Reference`) instead of a package reference (`PackageReference`) to refer to `System.Net.Http`. An unnecessary reference to `System.Runtime` was removed.
- The stream processor now propagates an exception out of its initialization `Task` if it encounters an unrecoverable error.

## [1.0.2] - 2018-07-24

''This release is broken and should not be used.''

## [1.0.1] - 2018-07-02

### Changed
- When targeting .NET 4.5, the dependency on `Newtonsoft.Json` now has a minimum version of 6.0.1 rather than 9.0.1. This should not affect any applications that specify a higher version for this assembly.

### Removed
- The `Identify` method is no longer part of `ILdCommonClient`, since it does not have the same signature in the Xamarin client as in the server-side .NET SDK.

## [1.0.0] - 2018-06-26

Initial release, corresponding to .net-client version 5.1.0.
