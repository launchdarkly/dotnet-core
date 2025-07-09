using System;
using Xunit;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    public class PluginMetadataTest
    {
        [Fact]
        public void CanConstructWithValidName()
        {
            var pluginMetadata = new PluginMetadata("test-plugin");
            
            Assert.Equal("test-plugin", pluginMetadata.Name);
        }

        [Fact]
        public void NoExceptionForNullName()
        {
            var pluginMetadata = new PluginMetadata(null);
            Assert.Null(pluginMetadata.Name);
        }
    }
} 