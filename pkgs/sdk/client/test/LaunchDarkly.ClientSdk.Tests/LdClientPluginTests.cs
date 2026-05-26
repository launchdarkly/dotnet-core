using System.Collections.Generic;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Plugins;
using LaunchDarkly.Sdk.Integrations.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    public class LdClientPluginTests : BaseTest
    {
        public LdClientPluginTests(ITestOutputHelper testOutput) : base(testOutput) { }

        [Fact]
        public void RegisterIsCalledForSinglePlugin()
        {
            var plugin = new SpyPlugin("spy");
            var config = BasicConfig()
                .Plugins(new PluginConfigurationBuilder().Add(plugin))
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(plugin.Registered);
                Assert.NotNull(plugin.ReceivedClient);
                Assert.NotNull(plugin.ReceivedMetadata);
            }
        }

        [Fact]
        public void RegisterIsCalledForMultiplePlugins()
        {
            var plugin1 = new SpyPlugin("first");
            var plugin2 = new SpyPlugin("second");
            var config = BasicConfig()
                .Plugins(new PluginConfigurationBuilder().Add(plugin1).Add(plugin2))
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(plugin1.Registered);
                Assert.True(plugin2.Registered);
            }
        }

        [Fact]
        public void RegisterReceivesClientInstance()
        {
            var plugin = new SpyPlugin("spy");
            var config = BasicConfig()
                .Plugins(new PluginConfigurationBuilder().Add(plugin))
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.Same(client, plugin.ReceivedClient);
            }
        }

        [Fact]
        public void RegisterReceivesEnvironmentMetadata()
        {
            var plugin = new SpyPlugin("spy");
            var config = BasicConfig()
                .Plugins(new PluginConfigurationBuilder().Add(plugin))
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.NotNull(plugin.ReceivedMetadata);
                Assert.Equal(BasicMobileKey, plugin.ReceivedMetadata.Credential);
                Assert.Equal(CredentialType.MobileKey, plugin.ReceivedMetadata.CredentialType);
            }
        }

        [Fact]
        public void NoPluginsConfiguredDoesNotCauseError()
        {
            var config = BasicConfig()
                .Plugins(new PluginConfigurationBuilder())
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.NotNull(client);
            }
        }

        [Fact]
        public void PluginHooksAreCollected()
        {
            var hook = new StubHook("plugin-hook");
            var plugin = new SpyPlugin("spy", new List<Hook> { hook });
            var config = BasicConfig()
                .Plugins(new PluginConfigurationBuilder().Add(plugin))
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(plugin.Registered);
                Assert.True(plugin.GetHooksCalled);
            }
        }

        [Fact]
        public void FailingPluginRegisterDoesNotPreventOtherPlugins()
        {
            var badPlugin = new FailingPlugin("bad");
            var goodPlugin = new SpyPlugin("good");
            var config = BasicConfig()
                .Plugins(new PluginConfigurationBuilder().Add(badPlugin).Add(goodPlugin))
                .Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.True(goodPlugin.Registered);
            }
        }

        private class SpyPlugin : Plugin
        {
            public bool Registered { get; private set; }
            public bool GetHooksCalled { get; private set; }
            public ILdClient ReceivedClient { get; private set; }
            public EnvironmentMetadata ReceivedMetadata { get; private set; }

            private readonly IList<Hook> _hooks;

            public SpyPlugin(string name, IList<Hook> hooks = null) : base(name)
            {
                _hooks = hooks ?? new List<Hook>();
            }

            public override void Register(ILdClient client, EnvironmentMetadata metadata)
            {
                Registered = true;
                ReceivedClient = client;
                ReceivedMetadata = metadata;
            }

            public override IList<Hook> GetHooks(EnvironmentMetadata metadata)
            {
                GetHooksCalled = true;
                return _hooks;
            }
        }

        private class StubHook : Hook
        {
            public StubHook(string name) : base(name) { }
        }

        private class FailingPlugin : Plugin
        {
            public FailingPlugin(string name) : base(name) { }

            public override void Register(ILdClient client, EnvironmentMetadata metadata)
            {
                throw new System.Exception("intentional failure");
            }
        }
    }
}
