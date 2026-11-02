# SDK contract test service

This directory contains an implementation of the cross-platform SDK testing protocol defined by https://github.com/launchdarkly/sdk-test-harness. See that project's `README` for details of this protocol, and the kinds of SDK capabilities that are relevant to the contract tests. This code should not need to be updated unless the SDK has added or removed such capabilities.

To run these tests locally, run `make contract-tests` from the SDK project root directory. This downloads the correct version of the test harness tool automatically.

Or, to test against an in-progress local version of the test harness, run `make start-contract-test-service` from the SDK project root directory; then, in the root directory of the `sdk-test-harness` project, build the test harness and run it from the command line.

Currently, the project does _not_ automatically detect the available target frameworks. It will default to building and running for .NET 8.0. To use a different target framework, set the environment variable `TESTFRAMEWORK` to the name of the application runtime framework (such as `net8.0`), and set the environment variable `BUILDFRAMEWORKS` (note the S at the end) to the target framework that the SDK should be built for (which may or may not be the same).

The service references the Redis, DynamoDB, and Consul integrations in this repository, built against the local SDK rather than the published SDK package that they reference by default. Those integrations target `netstandard2.0` and `net462`, so if you set `BUILDFRAMEWORKS`, it must include both.

To run the persistent store tests, start the stores on their default ports, for example with `docker run -p 6379:6379 redis`, `docker run -p 8000:8000 amazon/dynamodb-local`, and `docker run -p 8500:8500 hashicorp/consul`, and pass `-enable-persistence-tests` to the test harness. DynamoDB uses port 8000, the service's default port, so pass another port as the service's first argument, for example `dotnet bin/Debug/net8.0/ContractTestService.dll 9000`.
