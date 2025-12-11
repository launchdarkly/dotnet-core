// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using LaunchDarkly.Sdk;
using LaunchDarkly.Sdk.Server;

var config = Configuration.Builder(Environment.GetEnvironmentVariable("LAUNCHDARKLY_SDK_KEY"))
    .DataSystem(Components.DataSystem().Default()).Build();

Console.WriteLine("Initializing client");
var client = new LdClient(config);

Console.WriteLine($"Initialized: {client.Initialized}");

client.FlagTracker.FlagValueChangeHandler("my-boolean-flag", Context.New("bob"), ((sender, @event) =>
{
    Console.WriteLine("Got change", @event.Key, @event.NewValue);
}));

Console.WriteLine("Evaluate {0}", client.BoolVariation("my-boolean-flag", Context.New("bob"), false));;

Console.ReadLine();