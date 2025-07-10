using Xunit;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    public class ApplicationMetadataTest
    {
        [Fact]
        public void CanConstructWithAllParameters()
        {
            var appMetadata = new ApplicationMetadata("app-id", "1.0.0", "My App", "v1.0.0");

            Assert.Equal("app-id", appMetadata.Id);
            Assert.Equal("1.0.0", appMetadata.Version);
            Assert.Equal("My App", appMetadata.Name);
            Assert.Equal("v1.0.0", appMetadata.VersionName);
        }

        [Fact]
        public void CanConstructWithDefaultParameters()
        {
            var appMetadata = new ApplicationMetadata();

            Assert.Null(appMetadata.Id);
            Assert.Null(appMetadata.Version);
            Assert.Null(appMetadata.Name);
            Assert.Null(appMetadata.VersionName);
        }
    }
}
