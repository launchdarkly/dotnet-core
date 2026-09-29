using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Plugins;
using LaunchDarkly.Sdk.Integrations.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    using SeriesData = ImmutableDictionary<string, object>;

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

        [Fact]
        public void RegisterPluginRegistersPluginAndItsHooks()
        {
            var hook = new RecordingHook("plugin-hook");
            var plugin = new SpyPlugin("spy", new List<Hook> { hook });
            var config = BasicConfig().Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                // Nothing happens until the plugin is registered, since it was not configured.
                Assert.False(plugin.Registered);

                client.RegisterPlugin(plugin);

                Assert.True(plugin.Registered);
                Assert.Same(client, plugin.ReceivedClient);
                Assert.Equal(BasicMobileKey, plugin.ReceivedMetadata.Credential);

                client.BoolVariation("flag-key", false);
                Assert.Equal(1, hook.BeforeEvaluationCount);
            }
        }

        [Fact]
        public void RegisterPluginRunsTheRegisteringPluginsOwnHooks()
        {
            // Evaluates a flag from inside Register, so the test can tell whether this plugin's own
            // hooks were live at that point.
            var hook = new RecordingHook("plugin-hook");
            var plugin = new EvaluateOnRegisterPlugin("evaluates", hook);
            var config = BasicConfig().Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                client.RegisterPlugin(plugin);

                // The hooks are live by the time Register runs, as they are for a configured plugin.
                Assert.Equal(1, hook.BeforeEvaluationCount);

                // And they keep running for evaluations made after registration.
                client.BoolVariation("flag-key", false);
                Assert.Equal(2, hook.BeforeEvaluationCount);
            }
        }

        [Fact]
        public void RegisterPluginKeepsHooksWhenRegisterThrows()
        {
            var hook = new RecordingHook("plugin-hook");
            var plugin = new FailingPlugin("bad", new List<Hook> { hook });
            var config = BasicConfig().Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                // The exception is logged rather than propagated.
                client.RegisterPlugin(plugin);

                // The hooks were already live when Register threw, so they stay live, as they do for
                // a configured plugin whose Register throws.
                client.BoolVariation("flag-key", false);
                Assert.Equal(1, hook.BeforeEvaluationCount);
            }
        }

        [Fact]
        public void RegisterPluginDoesNotRegisterPluginWhoseGetHooksThrows()
        {
            var plugin = new FailingGetHooksPlugin("bad-hooks");
            var config = BasicConfig().Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                client.RegisterPlugin(plugin);

                Assert.False(plugin.Registered);
            }
        }

        [Fact]
        public void RegisterPluginRejectsNullPlugin()
        {
            var config = BasicConfig().Build();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                Assert.Throws<ArgumentNullException>(() => client.RegisterPlugin(null));
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

        /// <summary>
        /// Counts the evaluations it sees, so a test can tell when a hook became live.
        /// </summary>
        private class RecordingHook : Hook
        {
            public int BeforeEvaluationCount { get; private set; }

            public RecordingHook(string name) : base(name) { }

            public override SeriesData BeforeEvaluation(EvaluationSeriesContext context, SeriesData data)
            {
                BeforeEvaluationCount++;
                return data;
            }
        }

        /// <summary>
        /// Evaluates a flag from inside <c>Register</c>, so a test can tell whether this plugin's own
        /// hooks were live at that point.
        /// </summary>
        private class EvaluateOnRegisterPlugin : Plugin
        {
            private readonly IList<Hook> _hooks;

            public EvaluateOnRegisterPlugin(string name, Hook hook) : base(name)
            {
                _hooks = new List<Hook> { hook };
            }

            public override void Register(ILdClient client, EnvironmentMetadata metadata)
            {
                client.BoolVariation("flag-key", false);
            }

            public override IList<Hook> GetHooks(EnvironmentMetadata metadata) => _hooks;
        }

        private class FailingPlugin : Plugin
        {
            private readonly IList<Hook> _hooks;

            public FailingPlugin(string name, IList<Hook> hooks = null) : base(name)
            {
                _hooks = hooks ?? new List<Hook>();
            }

            public override void Register(ILdClient client, EnvironmentMetadata metadata)
            {
                throw new System.Exception("intentional failure");
            }

            public override IList<Hook> GetHooks(EnvironmentMetadata metadata) => _hooks;
        }

        private class FailingGetHooksPlugin : Plugin
        {
            public bool Registered { get; private set; }

            public FailingGetHooksPlugin(string name) : base(name) { }

            public override void Register(ILdClient client, EnvironmentMetadata metadata)
            {
                Registered = true;
            }

            public override IList<Hook> GetHooks(EnvironmentMetadata metadata)
            {
                throw new System.Exception("intentional failure");
            }
        }
    }
}
