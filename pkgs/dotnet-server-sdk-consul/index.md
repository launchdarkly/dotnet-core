The [`LaunchDarkly.ServerSdk.Consul`](https://nuget.org/packages/LaunchDarkly.ServerSdk.Consul) package provides a Consul-backed persistence mechanism (data store) for the [LaunchDarkly .NET SDK](https://github.com/launchdarkly/dotnet-server-sdk), replacing the default in-memory data store. The underlying Consul client implementation is https://github.com/PlayFab/consuldotnet.

For more information, see also: [Using Consul as a persistent feature store](https://docs.launchdarkly.com/sdk/features/storing-data/consul#net-server-side).

Version 2.0.0 and above of this library works with version 6.0.0 and above of the LaunchDarkly .NET SDK. For earlier versions of the SDK, use the latest 1.x release of this library.

The entry point for using this integration is the **<xref:LaunchDarkly.Sdk.Server.Integrations.Consul>** class in <xref:LaunchDarkly.Sdk.Server.Integrations>.

## Quick setup

This assumes that you have already installed the LaunchDarkly .NET SDK.

1. Add the NuGet package [`LaunchDarkly.ServerSdk.Consul`](https://nuget.org/packages/LaunchDarkly.ServerSdk.Consul) to your project.

2. Import the package (note that the namespace is different from the package name):

```csharp
        using LaunchDarkly.Sdk.Server.Integrations;
```

3. When configuring your `LdClient`, add the Consul data store as a `PersistentDataStore`. You may specify any custom Consul options using the methods of `ConsulDataStoreBuilder`. For instance, to customize the Consul host address:

```csharp
        var ldConfig = Configuration.Default("YOUR_SDK_KEY")
            .DataStore(
                Components.PersistentDataStore(
                    Consul.DataStore().Address("http://my-consul-host:8500")
                )
            )
            .Build();
        var ldClient = new LdClient(ldConfig);
```

By default, the store will try to connect to a local Consul instance on port 8500.

## Caching behavior

The LaunchDarkly SDK has a standard caching mechanism for any persistent data store, to reduce database traffic. This is configured through the SDK's `PersistentDataStoreBuilder` class as described in the SDK documentation. For instance, to specify a cache TTL of 5 minutes:

```csharp
        var config = Configuration.Default("YOUR_SDK_KEY")
            .DataStore(
                Components.PersistentDataStore(
                    Consul.DataStore().Address("http://my-consul-host:8500")
                ).CacheTime(TimeSpan.FromMinutes(5))
            )
            .Build();
```

By default, the store will try to connect to a local Consul instance on port 8500.
