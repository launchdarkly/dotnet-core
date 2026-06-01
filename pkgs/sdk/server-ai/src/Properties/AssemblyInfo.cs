using System.Runtime.CompilerServices;

#if DEBUG
// Allow unit tests to see internal classes (note, the test assembly is not signed;
// tests must be run against the Debug configuration of this assembly)
[assembly: InternalsVisibleTo("LaunchDarkly.ServerSdk.Ai.Tests")]

// Allow mock/proxy objects in unit tests to access internal classes
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
#endif
