# Changelog

## [0.7.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.6.0...LaunchDarkly.ServerSdk.Ai-v0.7.0) (2025-02-06)


### Features

* Add variation version to AI metric data ([#71](https://github.com/launchdarkly/dotnet-core/issues/71)) ([ac3e927](https://github.com/launchdarkly/dotnet-core/commit/ac3e927ae36546cf0b849abd60f2c21bb5864daa))

## [0.6.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.5.0...LaunchDarkly.ServerSdk.Ai-v0.6.0) (2025-01-28)


### Features

* track TimeToFirstToken in LdAiConfigTracker ([#67](https://github.com/launchdarkly/dotnet-core/issues/67)) ([875dba5](https://github.com/launchdarkly/dotnet-core/commit/875dba5bc398085d3569a1f78f0039c81916217b))

## [0.5.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.4.0...LaunchDarkly.ServerSdk.Ai-v0.5.0) (2024-12-17)


### Features

* Add `TrackError` to mirror `TrackSuccess` ([#64](https://github.com/launchdarkly/dotnet-core/issues/64)) ([7acc574](https://github.com/launchdarkly/dotnet-core/commit/7acc574a56299a2058c1a357d54d3df5091a8f02))

## [0.4.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.3.2...LaunchDarkly.ServerSdk.Ai-v0.4.0) (2024-12-10)


### ⚠ BREAKING CHANGES

* rename model.id and provider.id to name ([#61](https://github.com/launchdarkly/dotnet-core/issues/61))
* rename _ldMeta.versionKey to variationKey ([#62](https://github.com/launchdarkly/dotnet-core/issues/62))

### Code Refactoring

* rename _ldMeta.versionKey to variationKey ([#62](https://github.com/launchdarkly/dotnet-core/issues/62)) ([3f7089d](https://github.com/launchdarkly/dotnet-core/commit/3f7089d6541c976d03e1040940a1350f5bd0d63c))
* rename model.id and provider.id to name ([#61](https://github.com/launchdarkly/dotnet-core/issues/61)) ([a0d0705](https://github.com/launchdarkly/dotnet-core/commit/a0d07058eb0b8afff2b46dba385e73cac23b6bcd))

## [0.3.2](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.3.1...LaunchDarkly.ServerSdk.Ai-v0.3.2) (2024-11-25)


### Bug Fixes

* add setter for ModelId ([#54](https://github.com/launchdarkly/dotnet-core/issues/54)) ([bb6a1e9](https://github.com/launchdarkly/dotnet-core/commit/bb6a1e9a5bebc70ea4b78d8853fe66f6d8738c1c))

## [0.3.1](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.3.0...LaunchDarkly.ServerSdk.Ai-v0.3.1) (2024-11-22)


### Bug Fixes

* rename ModelConfig method to Config ([#52](https://github.com/launchdarkly/dotnet-core/issues/52)) ([a98db42](https://github.com/launchdarkly/dotnet-core/commit/a98db42d57bac140f323b7ce13b385e74cee2bd7))

## [0.3.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.2.0...LaunchDarkly.ServerSdk.Ai-v0.3.0) (2024-11-22)


### Features

* update AI SDK with latest spec changes ([#50](https://github.com/launchdarkly/dotnet-core/issues/50)) ([b1a3a8c](https://github.com/launchdarkly/dotnet-core/commit/b1a3a8c8e8be0c0cc092ad5329b33a07019e8119))

## [0.2.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.1.1...LaunchDarkly.ServerSdk.Ai-v0.2.0) (2024-11-19)


### Features

* support multi-kind contexts in template interpolation ([#48](https://github.com/launchdarkly/dotnet-core/issues/48)) ([40ff539](https://github.com/launchdarkly/dotnet-core/commit/40ff5393d28033808bd026144921bd87198fa93a))

## [0.1.1](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.1.0...LaunchDarkly.ServerSdk.Ai-v0.1.1) (2024-11-18)


### Bug Fixes

* catch exceptions thrown by template interpolation ([#43](https://github.com/launchdarkly/dotnet-core/issues/43)) ([7a6cfd5](https://github.com/launchdarkly/dotnet-core/commit/7a6cfd503f517b5a024d4746d4fc5bfcd1a4ba69))

## 0.1.0 (2024-11-12)


### Features

* release server-ai ([#38](https://github.com/launchdarkly/dotnet-core/issues/38)) ([cf07fef](https://github.com/launchdarkly/dotnet-core/commit/cf07fef86f6ce86ed2e76f2a9f7115617f0e0738))
