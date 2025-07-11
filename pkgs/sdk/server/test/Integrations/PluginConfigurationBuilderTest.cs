using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Integrations.Plugins;
using LaunchDarkly.Sdk.Server.Hooks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Plugins;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    // Mock plugin for testing
    public class TestPlugin : Plugin
    {
        public TestPlugin(string name = "test-plugin")
            :base(name) { }

        public override void Register(ILdClient client, EnvironmentMetadata metadata)
        {
            // No-op for testing
        }
    }

    public class PluginConfigurationBuilderTest
    {
        [Fact]
        public void Constructor_WithNoParameters_CreatesEmptyConfiguration()
        {
            var builder = new PluginConfigurationBuilder();
            var config = builder.Build();

            Assert.NotNull(config.Plugins);
            Assert.Empty(config.Plugins);
        }

        [Fact]
        public void Constructor_WithPluginCollection_CreatesConfigurationWithPlugins()
        {
            var plugin1 = new TestPlugin("plugin1");
            var plugin2 = new TestPlugin("plugin2");
            var plugins = new List<Plugin> { plugin1, plugin2 };

            var builder = new PluginConfigurationBuilder(plugins);
            var config = builder.Build();

            Assert.Equal(2, config.Plugins.Count());
            Assert.Contains(plugin1, config.Plugins);
            Assert.Contains(plugin2, config.Plugins);
        }

        [Fact]
        public void Add_MultiplePlugins_AddsAllPluginsToConfiguration()
        {
            var builder = new PluginConfigurationBuilder();
            var plugin1 = new TestPlugin("plugin1");
            var plugin2 = new TestPlugin("plugin2");
            var plugin3 = new TestPlugin("plugin3");

            builder.Add(plugin1).Add(plugin2).Add(plugin3);
            var config = builder.Build();

            Assert.Equal(3, config.Plugins.Count());
            Assert.Contains(plugin1, config.Plugins);
            Assert.Contains(plugin2, config.Plugins);
            Assert.Contains(plugin3, config.Plugins);
        }

        [Fact]
        public void Add_ReturnsBuilderInstance_AllowsFluentInterface()
        {
            var builder = new PluginConfigurationBuilder();
            var plugin = new TestPlugin("test-plugin");

            var returnedBuilder = builder.Add(plugin);

            Assert.Same(builder, returnedBuilder);
        }

        [Fact]
        public void Build_CanBeCalledMultipleTimes()
        {
            var builder = new PluginConfigurationBuilder();
            var plugin = new TestPlugin("test-plugin");
            builder.Add(plugin);

            var config1 = builder.Build();
            var config2 = builder.Build();

            Assert.Single(config1.Plugins);
            Assert.Single(config2.Plugins);
            Assert.Contains(plugin, config1.Plugins);
            Assert.Contains(plugin, config2.Plugins);
        }

        [Fact]
        public void Build_ModifyingBuilderAfterBuild_DoesNotAffectPreviousConfiguration()
        {
            var builder = new PluginConfigurationBuilder();
            var plugin1 = new TestPlugin("plugin1");
            var plugin2 = new TestPlugin("plugin2");

            builder.Add(plugin1);
            var config1 = builder.Build();

            builder.Add(plugin2);
            var config2 = builder.Build();

            Assert.Single(config1.Plugins);
            Assert.Equal(2, config2.Plugins.Count());
            Assert.Contains(plugin1, config1.Plugins);
            Assert.DoesNotContain(plugin2, config1.Plugins);
        }

        [Fact]
        public void Constructor_WithEmptyPluginCollection_CreatesEmptyConfiguration()
        {
            var builder = new PluginConfigurationBuilder(new List<Plugin>());
            var config = builder.Build();

            Assert.NotNull(config.Plugins);
            Assert.Empty(config.Plugins);
        }

        [Fact]
        public void Constructor_WithNullPluginCollection_CreatesEmptyConfiguration()
        {
            var builder = new PluginConfigurationBuilder(null);
            var config = builder.Build();

            Assert.NotNull(config.Plugins);
            Assert.Empty(config.Plugins);
        }
    }
}
