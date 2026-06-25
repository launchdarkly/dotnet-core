using System;
using Xunit;

namespace LaunchDarkly.Sdk.Internal
{
    public class AssemblyVersionsTest
    {
        [Fact]
        public void GetVersionString()
        {
            // Starting in .NET 8, the commit sha is appended to the version string.
            var versionString = AssemblyVersions.GetAssemblyVersionStringForType(typeof(AssemblyVersionsTest)).Split('+')[0];

            Assert.Equal(
                "1.2.3", // this is hard-coded in LaunchDarkly.InternalSdk.Tests.csproj
                versionString
                );
        }

        [Fact]
        public void GetVersion()
        {
            Assert.Equal(
                new Version("1.2.3.0"),
                AssemblyVersions.GetAssemblyVersionForType(typeof(AssemblyVersionsTest))
                );
        }
    }
}
