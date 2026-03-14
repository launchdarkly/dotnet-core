// See https://aka.ms/new-console-template for more information

using dotnet_server_test_app.Configuration;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;

Configuration config;
try
{
    config = SdkConfigurationBuilder.BuildFromEnvironment();
}
catch (Exception ex)
{
    Console.WriteLine($"Configuration Error: {ex.Message}");
    return 1;
}

Console.WriteLine("Initializing client");
var client = new LdClient(config);

client.DataSourceStatusProvider.StatusChanged += (sender, status) =>
{
    Console.WriteLine($"Status changed: {status}");
};

client.DataStoreStatusProvider.StatusChanged += (sender, status) =>
{
    Console.WriteLine($"Status changed: {status}");
};

Console.WriteLine($"Data source status {client.DataSourceStatusProvider.Status}");
Console.WriteLine($"Data store status {client.DataStoreStatusProvider.Status}");

Console.WriteLine($"Initialized: {client.Initialized}");

Console.WriteLine($"my-boolean-flag: {client.BoolVariation("my-boolean-flag", Context.New("bob"), false)}");

client.FlagTracker.FlagChanged += client.FlagTracker.FlagValueChangeHandler("my-boolean-flag", Context.New("bob"),
    ((sender, @event) => { Console.WriteLine("Got change {0}, {1}", @event.Key, @event.NewValue); }));

Console.WriteLine("Evaluate {0}", client.BoolVariation("my-boolean-flag", Context.New("bob"), false));

while (true)
{
    Console.ReadLine();

    Console.WriteLine($"my-boolean-flag: {client.BoolVariation("my-boolean-flag", Context.New("bob"), false)}");
}
