using System;
using System.Runtime.CompilerServices;

#if DEBUG
// Allow unit tests to see internal classes. The test assemblies are not
// strong-named, so tests must be run against the Debug configuration of
// this assembly.

[assembly: InternalsVisibleTo("LaunchDarkly.ClientSdk.Tests")]
[assembly: InternalsVisibleTo("LaunchDarkly.ClientSdk.Device.Tests")]
#endif
