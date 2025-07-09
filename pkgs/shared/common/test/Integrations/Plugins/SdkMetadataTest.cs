using Xunit;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    public class SdkMetadataTest
    {

        [Fact]
        public void CanConstructWithAllParameters()
        {
            var sdkMetadata = new SdkMetadata("dotnet-server-sdk", "6.0.0", "my-wrapper", "2.0.0");

            Assert.Equal("dotnet-server-sdk", sdkMetadata.Name);
            Assert.Equal("6.0.0", sdkMetadata.Version);
            Assert.Equal("my-wrapper", sdkMetadata.WrapperName);
            Assert.Equal("2.0.0", sdkMetadata.WrapperVersion);
        }

        [Fact]
        public void CanHandleNullValues()
        {
            var sdkMetadata = new SdkMetadata(null, null);

            Assert.Null(sdkMetadata.Name);
            Assert.Null(sdkMetadata.Version);
            Assert.Null(sdkMetadata.WrapperName);
            Assert.Null(sdkMetadata.WrapperVersion);
        }
    }
}
