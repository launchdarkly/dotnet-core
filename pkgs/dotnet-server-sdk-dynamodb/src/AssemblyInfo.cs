using System.Runtime.CompilerServices;

#if DEBUG
// Allow unit tests to see internal classes
[assembly: InternalsVisibleTo("LaunchDarkly.ServerSdk.DynamoDB.Tests")]
#endif
