using Xunit;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    public class EnvironmentMetadataTest
    {
        [Fact]
        public void CanConstructWithAllParameters()
        {
            var sdkMetadata = new SdkMetadata("dotnet-server-sdk", "6.0.0");
            var appMetadata = new ApplicationMetadata("app-id", "1.0.0");
            var envMetadata = new EnvironmentMetadata(sdkMetadata, "test-sdk-key", CredentialType.SdkKey, appMetadata);
            
            Assert.Equal(sdkMetadata, envMetadata.Sdk);
            Assert.Equal("test-sdk-key", envMetadata.Credential);
            Assert.Equal(CredentialType.SdkKey, envMetadata.CredentialType);
            Assert.Equal(appMetadata, envMetadata.Application);
        }
    }
} 