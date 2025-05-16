using System.Reflection;
using System.Runtime.CompilerServices;

#if DEBUG
// Allow unit tests to see internal classes (note, the test assembly is not signed;
// tests must be run against the Debug configuration of this assembly)
[assembly: InternalsVisibleTo("LaunchDarkly.ServerSdk.Redis.Tests")]
#endif
