LaunchDarkly Server-Side .NET SDK Shared Test Code
==================================================

This project provides support code for testing LaunchDarkly .NET SDK integrations. Feature store implementations, etc., should use this code whenever possible to ensure consistent test coverage and avoid repetition. An example of a project using this code is [dotnet-server-sdk-redis](https://github.com/launchdarkly/dotnet-server-sdk-redis).

The code is not published to NuGet, since it isn't of any use in any non-test context. Instead, it's meant to be used as a Git subtree. Add the subtree to your project like this:

    git remote add dotnet-server-sdk-shared-tests git@github.com:launchdarkly/dotnet-server-sdk-shared-tests.git
    git subtree add --squash --prefix=dotnet-server-sdk-shared-tests/ dotnet-server-sdk-shared-tests master

Now you can add the project `LaunchDarkly.ServerSdk.SharedTests.csproj` within the subtree to your solution, and reference its classes in your tests.

To update the copy of `dotnet-server-sdk-shared-tests` in your repository to reflect changes in this one:

    git subtree pull --squash --prefix=dotnet-server-sdk-shared-tests/ dotnet-server-sdk-shared-tests master
