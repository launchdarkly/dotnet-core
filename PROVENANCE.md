## Verifying SDK build provenance with GitHub artifact attestations

LaunchDarkly uses [GitHub artifact attestations](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds) to help developers make their supply chain more secure by ensuring the authenticity and build integrity of our published SDK packages.

LaunchDarkly publishes provenance about our SDK package builds using [GitHub's `actions/attest` action](https://github.com/actions/attest). These attestations are stored in GitHub's attestation API and can be verified using the [GitHub CLI](https://cli.github.com/).

To verify build provenance attestations, we recommend using the [GitHub CLI `attestation verify` command](https://cli.github.com/manual/gh_attestation_verify). Example usage for verifying SDK packages is included below. This repository contains multiple packages; the example below uses the Server SDK as a reference.

```
# Set the version of the SDK to verify
SDK_VERSION=8.11.1
```

```
# Download the nupkg from NuGet
$ nuget install LaunchDarkly.ServerSdk -Version $SDK_VERSION -OutputDirectory ./packages

# Verify provenance using the GitHub CLI
$ gh attestation verify ./packages/LaunchDarkly.ServerSdk.${SDK_VERSION}/LaunchDarkly.ServerSdk.${SDK_VERSION}.nupkg --owner launchdarkly
```

Below is a sample of expected output.

```
Loaded digest sha256:... for file://LaunchDarkly.ServerSdk.8.11.1.nupkg
Loaded 1 attestation from GitHub API

The following policy criteria will be enforced:
- Predicate type must match:................ https://slsa.dev/provenance/v1
- Source Repository Owner URI must match:... https://github.com/launchdarkly
- Subject Alternative Name must match regex: (?i)^https://github.com/launchdarkly/
- OIDC Issuer must match:................... https://token.actions.githubusercontent.com

✓ Verification succeeded!

The following 1 attestation matched the policy criteria

- Attestation #1
  - Build repo:..... launchdarkly/dotnet-core
  - Build workflow:. .github/workflows/release-please.yml
  - Signer repo:.... launchdarkly/dotnet-core
  - Signer workflow: .github/workflows/release-please.yml
```

The same verification process applies to all packages published from this repository:

| Package | NuGet Name |
|---------|-----------|
| Server SDK | `LaunchDarkly.ServerSdk` |
| Server SDK AI | `LaunchDarkly.ServerSdk.Ai` |
| Client SDK | `LaunchDarkly.ClientSdk` |
| Common SDK | `LaunchDarkly.CommonSdk` |
| Common SDK JsonNet | `LaunchDarkly.CommonSdk.JsonNet` |
| Server SDK Telemetry | `LaunchDarkly.ServerSdk.Telemetry` |
| Server SDK Consul | `LaunchDarkly.ServerSdk.Consul` |
| Server SDK DynamoDB | `LaunchDarkly.ServerSdk.DynamoDB` |
| Server SDK Redis | `LaunchDarkly.ServerSdk.Redis` |

For more information, see [GitHub's documentation on verifying artifact attestations](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds#verifying-artifact-attestations-with-the-github-cli).

**Note:** These instructions do not apply when building our SDKs from source.
