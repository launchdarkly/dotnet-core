# LaunchDarkly SDK Json.NET Adapter

The add-on package `LaunchDarkly.CommonSdk.JsonNet` allows JSON-serializable data types from LaunchDarkly .NET SDKs, such as `User` and `LdValue`, to be encoded and decoded correctly by the [Json.NET](https://www.newtonsoft.com/json) library (`Newtonsoft.Json`).

Earlier versions of the LaunchDarkly SDKs used Json.NET internally, so nothing additional was needed to make this work. However, in later SDK releases, the Json.NET dependency was removed and so these types do not contain the `[JsonConverter]` annotation that would tell Json.NET how to encode and decode them.

It is always possible to encode or decode these types explicitly using the `LaunchDarkly.Sdk.Json.LdJsonSerialization` class. But if you want them to be handled automatically by code that uses Json.NET, just do the following:

1. Install the package `LaunchDarkly.CommonSdk.JsonNet`.

2. Define a `JsonSerializerSettings` object that includes the LaunchDarkly JSON converter:

```csharp
    var settings = new Newtonsoft.Json.JsonSerializerSettings
    {
        Converters = new List<Newtonsoft.Json.JsonConverter>
        {
            LaunchDarkly.Sdk.Json.LdJsonNet.Converter
            // you may add any other custom converters you want here
        }
    };
```

3. You can reference this configuration in any individual Json.NET operation:

```csharp
    var json = JsonConvert.SerializeObject(someObject, settings);
```

4. Or, to make these settings the default for all Json.NET operations:

```csharp
    JsonConvert.DefaultSettings = () => settings;
```
