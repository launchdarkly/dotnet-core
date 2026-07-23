# Changelog

## [0.13.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.12.0...LaunchDarkly.ServerSdk.Ai-v0.13.0) (2026-07-23)


### ⚠ BREAKING CHANGES

* ILdAiConfigTracker.TrackDuration now takes a double instead of a float — custom ILdAiConfigTracker implementations must update their TrackDuration override signature

### Features

* Extend AiMetrics with optional ToolCalls and DurationMs (duration override) ([2e307c9](https://github.com/launchdarkly/dotnet-core/commit/2e307c9faa08038f82a80e6cac9d1dea1a194b99))
* Extend MetricSummary with accumulated ToolCalls and ResumptionToken ([2e307c9](https://github.com/launchdarkly/dotnet-core/commit/2e307c9faa08038f82a80e6cac9d1dea1a194b99))
* ILdAiConfigTracker.TrackDuration now takes a double instead of a float — custom ILdAiConfigTracker implementations must update their TrackDuration override signature ([2e307c9](https://github.com/launchdarkly/dotnet-core/commit/2e307c9faa08038f82a80e6cac9d1dea1a194b99))
* **server-ai:** stamp modelKey and modelVersion on AI usage events (… ([#308](https://github.com/launchdarkly/dotnet-core/issues/308)) ([9daa47a](https://github.com/launchdarkly/dotnet-core/commit/9daa47aea0945c58a80c67d0094527f7dd514dcf))
* TrackMetricsOf auto-tracks tool calls, honors DurationMs override, and aligns operation-vs-extractor failure semantics with the other SDKs ([2e307c9](https://github.com/launchdarkly/dotnet-core/commit/2e307c9faa08038f82a80e6cac9d1dea1a194b99))

## [0.12.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.11.0...LaunchDarkly.ServerSdk.Ai-v0.12.0) (2026-06-30)


### Features

* Add AgentGraph support to the AI SDK ([#292](https://github.com/launchdarkly/dotnet-core/issues/292)) ([c81b28f](https://github.com/launchdarkly/dotnet-core/commit/c81b28f6ea3fd7a87145142b3e5ade653e7a2ccd))
* Add template config methods to AI SDK ([#299](https://github.com/launchdarkly/dotnet-core/issues/299)) ([1b96e4a](https://github.com/launchdarkly/dotnet-core/commit/1b96e4ae9d8a7f43327860185fefe785697ab095))

## [0.11.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.10.0...LaunchDarkly.ServerSdk.Ai-v0.11.0) (2026-06-11)


### Features

* Add AgentConfig, AgentConfigs, and JudgeConfig methods to ILdAiClient ([#282](https://github.com/launchdarkly/dotnet-core/issues/282)) ([34293ca](https://github.com/launchdarkly/dotnet-core/commit/34293caf07bc0a50b3f9f095cd81229cdf807969))
* Add Tools property to LdAiCompletionConfig — parses the same tools block agents use, exposes IReadOnlyDictionary&lt;string, ToolConfig&gt; (empty when absent) ([69418c8](https://github.com/launchdarkly/dotnet-core/commit/69418c8d564d8bf9bb97c6529fa85bbe6c971781))
* Add TrackDurationOf, TrackMetricsOf, TrackJudgeResult, TrackToolCall ([#287](https://github.com/launchdarkly/dotnet-core/issues/287)) ([485976e](https://github.com/launchdarkly/dotnet-core/commit/485976e2b90d1b14a40e0fa7e31eea0ef2c1a416))


### Bug Fixes

* Silently override 'ldctx' in user-supplied template variables instead of warning and discarding it — the SDK context always wins, matches cross-SDK behavior ([69418c8](https://github.com/launchdarkly/dotnet-core/commit/69418c8d564d8bf9bb97c6529fa85bbe6c971781))

## [0.10.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.9.4...LaunchDarkly.ServerSdk.Ai-v0.10.0) (2026-06-05)


### ⚠ BREAKING CHANGES

* Move Role from LaunchDarkly.Sdk.Server.Ai.DataModel into LdAiConfigTypes
* Rename LdAiConfig.ModelConfiguration to LdAiConfigTypes.ModelConfig and LdAiConfig.ModelProvider to LdAiConfigTypes.ProviderConfig
* Move shared types nested in LdAiConfig into static LdAiConfigTypes — Message, ModelConfiguration, and ModelProvider
* Enforce at-most-once tracking — each metric type (duration, tokens, feedback, success/error, time-to-first-token) records once per tracker; duplicates are dropped with a warning
* Add ResumptionToken on ILdAiConfigTracker and ILdAiClient.CreateTracker(resumptionToken, context) for cross-process tracker reconstruction with the original runId
* Add per-execution runId on all AI track event payloads for billing isolation
* Change CompletionConfig to return LdAiCompletionConfig instead of ILdAiConfigTracker — obtain a tracker via config.CreateTracker()
* Default LdAiCompletionConfigDefault.Enabled to true per AISDK spec (was false on the LdAiConfig builder in 0.9.x)
* Remove ILdAiConfigTracker.Config property — read config fields from the LdAiCompletionConfig the caller already holds
* Make LdAiConfigTracker SDK-constructed only — the public constructor is removed; obtain trackers via config.CreateTracker() or ILdAiClient.CreateTracker(resumptionToken, context)
* Convert LdAiCompletionConfig and LdAiCompletionConfigDefault from records to classes — equality is reference-based instead of structural
* Remove LaunchDarkly.Sdk.Server.Ai.DataModel namespace — delete unused AiConfig, Meta, Model, Provider, and Message JSON DTO classes
* Split unified LdAiConfig into LdAiCompletionConfig (SDK output) and LdAiCompletionConfigDefault (user input) — Builder, New(), and Disabled move to the Default type; introduce abstract LdAiConfig and LdAiConfigDefault base types for future agent/judge modes

### Features

* Add MetricSummary property on ILdAiConfigTracker summarizing recorded metrics ([44ff485](https://github.com/launchdarkly/dotnet-core/commit/44ff485fcc3095b3adea81d142bc865cd0904d12))
* Add per-execution runId on all AI track event payloads for billing isolation ([44ff485](https://github.com/launchdarkly/dotnet-core/commit/44ff485fcc3095b3adea81d142bc865cd0904d12))
* Add ResumptionToken on ILdAiConfigTracker and ILdAiClient.CreateTracker(resumptionToken, context) for cross-process tracker reconstruction with the original runId ([44ff485](https://github.com/launchdarkly/dotnet-core/commit/44ff485fcc3095b3adea81d142bc865cd0904d12))
* Change CompletionConfig to return LdAiCompletionConfig instead of ILdAiConfigTracker — obtain a tracker via config.CreateTracker() ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Convert LdAiCompletionConfig and LdAiCompletionConfigDefault from records to classes — equality is reference-based instead of structural ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Default LdAiCompletionConfigDefault.Enabled to true per AISDK spec (was false on the LdAiConfig builder in 0.9.x) ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Enforce at-most-once tracking — each metric type (duration, tokens, feedback, success/error, time-to-first-token) records once per tracker; duplicates are dropped with a warning ([44ff485](https://github.com/launchdarkly/dotnet-core/commit/44ff485fcc3095b3adea81d142bc865cd0904d12))
* Make LdAiConfigTracker SDK-constructed only — the public constructor is removed; obtain trackers via config.CreateTracker() or ILdAiClient.CreateTracker(resumptionToken, context) ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Mode-mismatch detection — log a warning and return the caller's default when the flag's _ldMeta.mode does not match the requested mode (per sdk-specs[#229](https://github.com/launchdarkly/dotnet-core/issues/229)) ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Move Role from LaunchDarkly.Sdk.Server.Ai.DataModel into LdAiConfigTypes ([ac7fd06](https://github.com/launchdarkly/dotnet-core/commit/ac7fd06ffa078b701f19b2eb16982103207633ff))
* Move shared types nested in LdAiConfig into static LdAiConfigTypes — Message, ModelConfiguration, and ModelProvider ([ac7fd06](https://github.com/launchdarkly/dotnet-core/commit/ac7fd06ffa078b701f19b2eb16982103207633ff))
* Non-object variation handling — log an error and return the caller's default when the variation result is not an object ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Per-message interpolation fallback — a malformed Mustache template keeps raw content for that message rather than discarding the entire config ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Remove ILdAiConfigTracker.Config property — read config fields from the LdAiCompletionConfig the caller already holds ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Remove LaunchDarkly.Sdk.Server.Ai.DataModel namespace — delete unused AiConfig, Meta, Model, Provider, and Message JSON DTO classes ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Rename LdAiConfig.ModelConfiguration to LdAiConfigTypes.ModelConfig and LdAiConfig.ModelProvider to LdAiConfigTypes.ProviderConfig ([ac7fd06](https://github.com/launchdarkly/dotnet-core/commit/ac7fd06ffa078b701f19b2eb16982103207633ff))
* Split unified LdAiConfig into LdAiCompletionConfig (SDK output) and LdAiCompletionConfigDefault (user input) — Builder, New(), and Disabled move to the Default type; introduce abstract LdAiConfig and LdAiConfigDefault base types for future agent/judge modes ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))
* Tolerant LdValue parsing — missing or wrong-typed fields degrade to typed defaults instead of discarding the whole config ([92f799f](https://github.com/launchdarkly/dotnet-core/commit/92f799f6681772eb1b34dd808517ce98845f70c6))

## [0.9.4](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.9.3...LaunchDarkly.ServerSdk.Ai-v0.9.4) (2026-05-27)


### Bug Fixes

* exclude documentation files from NuGet package builds ([#263](https://github.com/launchdarkly/dotnet-core/issues/263)) ([cc86ad6](https://github.com/launchdarkly/dotnet-core/commit/cc86ad6bd54d2201db5171971946368cde8f45f8))

## [0.9.3](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.9.2...LaunchDarkly.ServerSdk.Ai-v0.9.3) (2026-03-05)


### Bug Fixes

* Make defaultValue optional with a disabled default ([#232](https://github.com/launchdarkly/dotnet-core/issues/232)) ([f69d420](https://github.com/launchdarkly/dotnet-core/commit/f69d42034bb960f83b831d2edf6788f70a20ceed))

## [0.9.2](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.9.1...LaunchDarkly.ServerSdk.Ai-v0.9.2) (2026-02-25)


### Bug Fixes

* Improve usage reporting ([#228](https://github.com/launchdarkly/dotnet-core/issues/228)) ([376b6b0](https://github.com/launchdarkly/dotnet-core/commit/376b6b0cbca28c7b49e5f64ee54b1f6d317d99fa))

## [0.9.1](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.9.0...LaunchDarkly.ServerSdk.Ai-v0.9.1) (2025-08-29)


### Bug Fixes

* Add usage tracking to config method ([#151](https://github.com/launchdarkly/dotnet-core/issues/151)) ([95e1e7b](https://github.com/launchdarkly/dotnet-core/commit/95e1e7b8df6d04e4b92068c6c144c5702a48f244))

## [0.9.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.8.0...LaunchDarkly.ServerSdk.Ai-v0.9.0) (2025-07-30)


### Features

* added provider and model to ai tracker ([#145](https://github.com/launchdarkly/dotnet-core/issues/145)) ([2ba2dd4](https://github.com/launchdarkly/dotnet-core/commit/2ba2dd4cd9c009dbb2c42a5f4792a6d0be8e84e6))


### Bug Fixes

* Remove deprecated track generation event ([#143](https://github.com/launchdarkly/dotnet-core/issues/143)) ([ac1bb78](https://github.com/launchdarkly/dotnet-core/commit/ac1bb7835e05ee26ed72251cc443e740cfe0b11d))

## [0.8.0](https://github.com/launchdarkly/dotnet-core/compare/LaunchDarkly.ServerSdk.Ai-v0.7.0...LaunchDarkly.ServerSdk.Ai-v0.8.0) (2025-06-02)


### Features

* Update to net8 ([ddae814](https://github.com/launchdarkly/dotnet-core/commit/ddae814250cb21e0de2b953808202addd7098c4c))

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
