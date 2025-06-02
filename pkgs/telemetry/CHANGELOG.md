# Changelog

## [1.2.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Telemetry-v1.1.0...LaunchDarkly.ServerSdk.Telemetry-v1.2.0) (2025-06-02)


### Features

* Update to net8 ([ddae814](https://github.com/launchdarkly/dotnet-core/commit/ddae814250cb21e0de2b953808202addd7098c4c))

## [1.1.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Telemetry-v1.0.0...LaunchDarkly.ServerSdk.Telemetry-v1.1.0) (2025-03-24)


### Features

* Add environment id support for the OTEL hook. ([#82](https://github.com/launchdarkly/dotnet-core/issues/82)) ([c2ed519](https://github.com/launchdarkly/dotnet-core/commit/c2ed519e64dacccad3e74445e4f3b132dd3f4edb))

## [1.0.0](https://github.com/launchdarkly/dotnet-server-sdk/compare/telemetry-0.2.1...telemetry-1.0.0) (2024-04-24)


### Code Refactoring

* remove unecessary space in TracingHook.cs ([#210](https://github.com/launchdarkly/dotnet-server-sdk/issues/210)) ([f6ad0ad](https://github.com/launchdarkly/dotnet-server-sdk/commit/f6ad0adf472421668558cc2d437045a7ae1b86cd))

## [0.2.1](https://github.com/launchdarkly/dotnet-server-sdk/compare/telemetry-0.2.0...telemetry-0.2.1) (2024-04-23)


### Bug Fixes

* telemetry activity source should use its own version ([#207](https://github.com/launchdarkly/dotnet-server-sdk/issues/207)) ([82dd679](https://github.com/launchdarkly/dotnet-server-sdk/commit/82dd6790cd96815d73be63e5d8fa8563b205a2ed))

## [0.2.0](https://github.com/launchdarkly/dotnet-server-sdk/compare/telemetry-v0.1.0...telemetry-0.2.0) (2024-04-23)


### Features

* add support for a Tracing hook implemented via System.Diagnostics, compatible with OpenTelemetry ([d9043db](https://github.com/launchdarkly/dotnet-server-sdk/commit/d9043dbd9b0b5d962843b14607cbe6c7a5d48e06))
