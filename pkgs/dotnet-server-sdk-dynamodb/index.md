The [`LaunchDarkly.ServerSdk.DynamoDb`](https://nuget.org/packages/LaunchDarkly.ServerSdk.DynamoDB) package provides a Consul-backed persistence mechanism (data store) for the [LaunchDarkly .NET SDK](https://github.com/launchdarkly/dotnet-server-sdk), replacing the default in-memory data store. It uses the [AWS SDK for .NET](https://aws.amazon.com/sdk-for-net/).

For more information, see also: [Using DynamoDB as a persistent feature store](https://docs.launchdarkly.com/sdk/features/storing-data/dynamodb#net).

Version 2.0.0 and above of this library works with version 6.0.0 and above of the LaunchDarkly .NET SDK. For earlier versions of the SDK, use the latest 1.x release of this library.

The entry point for using this integration is the **<xref:LaunchDarkly.Sdk.Server.Integrations.DynamoDB>** class in <xref:LaunchDarkly.Sdk.Server.Integrations>.

## Quick setup

This assumes that you have already installed the LaunchDarkly .NET SDK.

1. In DynamoDB, create a table which has the following schema: a partition key called **"namespace"** and a sort key called **"key"**, both with a string type. The LaunchDarkly library does not create the table automatically, because it has no way of knowing what additional properties (such as permissions and throughput) you would want it to have.

2. Add the NuGet package [`LaunchDarkly.ServerSdk.DynamoDB`](https://nuget.org/packages/LaunchDarkly.ServerSdk.DynamoDB) to your project.

3. Import the package (note that the namespace is different from the package name):

```csharp
        using LaunchDarkly.Sdk.Server.Integrations;
```

4. When configuring your `LdClient`, add the DynamoDB data store as a `PersistentDataStore`. You may specify any custom DynamoDB options using the methods of `DynamoDBDataStoreBuilder`. For instance, if you are passing in your AWS credentials programmatically from a variable called `myCredentials`:

```csharp
        var ldConfig = Configuration.Default("YOUR_SDK_KEY")
            .DataStore(
                Components.PersistentDataStore(
                    DynamoDB.DataStore("my-table-name").Credentials(myCredentials)
                )
            )
            .Build();
        var ldClient = new LdClient(ldConfig);
```

## Caching behavior

The LaunchDarkly SDK has a standard caching mechanism for any persistent data store, to reduce database traffic. This is configured through the SDK's `PersistentDataStoreBuilder` class as described in the SDK documentation. For instance, to specify a cache TTL of 5 minutes:

```csharp
        var config = Configuration.Default("YOUR_SDK_KEY")
            .DataStore(
                Components.PersistentDataStore(
                    DynamoDB.DataStore("my-table-name").Credentials(myCredentials)
                ).CacheTime(TimeSpan.FromMinutes(5))
            )
            .Build();
```
