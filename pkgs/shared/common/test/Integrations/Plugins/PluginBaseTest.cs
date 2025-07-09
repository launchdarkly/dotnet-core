using System.Collections.Generic;
using Xunit;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    public class PluginBaseTest
    {
        // Test implementation of PluginBase that uses default GetHooks
        public class TestPluginWithDefaultHooks : PluginBase<string, string>
        {
            public TestPluginWithDefaultHooks()
                : base("test-plugin") { }

            public override void Register(string client, EnvironmentMetadata metadata)
            {
                // No-op for testing
            }

            // Uses default GetHooks implementation
        }

        // Test implementation of PluginBase that overrides GetHooks
        public class TestPluginWithCustomHooks : PluginBase<string, string>
        {
            public TestPluginWithCustomHooks()
                : base("test-plugin-with-custom-hooks") { }
            public List<string> CustomHooks { get; set; } = new List<string>();

            public override void Register(string client, EnvironmentMetadata metadata)
            {
                // No-op for testing
            }

            public override IList<string> GetHooks(EnvironmentMetadata metadata)
            {
                return CustomHooks;
            }
        }

        [Fact]
        public void DefaultGetHooks_ReturnsEmptyList()
        {
            var plugin = new TestPluginWithDefaultHooks();
            var metadata = new EnvironmentMetadata(
                new SdkMetadata("test-sdk", "1.0.0"),
                "test-key",
                CredentialType.SdkKey,
                new ApplicationMetadata("test-app", "1.0.0")
            );

            var hooks = plugin.GetHooks(metadata);

            Assert.NotNull(hooks);
            Assert.Empty(hooks);
        }

        [Fact]
        public void CustomGetHooks_ReturnsProvidedHooks()
        {
            var plugin = new TestPluginWithCustomHooks();
            plugin.CustomHooks.Add("hook1");
            plugin.CustomHooks.Add("hook2");
            
            var metadata = new EnvironmentMetadata(
                new SdkMetadata("test-sdk", "1.0.0"),
                "test-key",
                CredentialType.SdkKey,
                new ApplicationMetadata("test-app", "1.0.0")
            );

            var hooks = plugin.GetHooks(metadata);

            Assert.Equal(2, hooks.Count);
            Assert.Contains("hook1", hooks);
            Assert.Contains("hook2", hooks);
        }

        [Fact]
        public void GetMetadata_ReturnsExpectedMetadata()
        {
            var plugin = new TestPluginWithDefaultHooks();

            Assert.Equal("test-plugin", plugin.Metadata.Name);
        }

        [Fact]
        public void Register_CanBeCalledWithoutException()
        {
            var plugin = new TestPluginWithDefaultHooks();
            var metadata = new EnvironmentMetadata(
                new SdkMetadata("test-sdk", "1.0.0"),
                "test-key",
                CredentialType.SdkKey,
                new ApplicationMetadata("test-app", "1.0.0")
            );

            // Should not throw exception
            plugin.Register("test-client", metadata);
        }
    }
} 