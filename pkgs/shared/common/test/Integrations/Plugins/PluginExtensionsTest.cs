using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    // Mock client for testing
    public class MockClient
    {
        public List<string> RegisteredPlugins { get; } = new List<string>();
    }

    // Mock hook for testing
    public class MockHook
    {
        public string Name { get; set; }
    }

    // Mock plugin implementation for testing
    public class MockPlugin : PluginBase<MockClient, MockHook>
    {
        public MockPlugin(string name)
            : base(name) { }
        public List<MockHook> Hooks { get; set; } = new List<MockHook>();
        public bool ThrowOnRegister { get; set; } = false;
        public bool ThrowOnGetHooks { get; set; } = false;
        public bool ReturnNullHooks { get; set; } = false;

        public override void Register(MockClient client, EnvironmentMetadata metadata)
        {
            if (ThrowOnRegister)
                throw new InvalidOperationException("Test exception");
            
            client.RegisteredPlugins.Add(Metadata.Name);
        }

        public override IList<MockHook> GetHooks(EnvironmentMetadata metadata)
        {
            if (ThrowOnGetHooks)
                throw new InvalidOperationException("Test exception");
            
            if (ReturnNullHooks)
                return null;
            
            return Hooks;
        }
    }

    public class PluginExtensionsTest
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly Logger _logger;
        private readonly MockClient _client;
        private readonly EnvironmentMetadata _environmentMetadata;

        public PluginExtensionsTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
            _logger = TestLogging.TestLogger(testOutputHelper);
            _client = new MockClient();
            _environmentMetadata = new EnvironmentMetadata(
                new SdkMetadata("test-sdk", "1.0.0"),
                "test-key",
                CredentialType.SdkKey,
                new ApplicationMetadata("test-app", "1.0.0")
            );
        }

        [Fact]
        public void RegisterPlugins_SuccessfullyRegistersAllPlugins()
        {
            var plugin1 = new MockPlugin("plugin1");
            var plugin2 = new MockPlugin("plugin2");
            var plugins = new List<MockPlugin> { plugin1, plugin2 };

            _client.RegisterPlugins(plugins, _environmentMetadata, _logger);

            Assert.Contains("plugin1", _client.RegisteredPlugins);
            Assert.Contains("plugin2", _client.RegisteredPlugins);
            Assert.Equal(2, _client.RegisteredPlugins.Count);
        }

        [Fact]
        public void RegisterPlugins_ContinuesAfterExceptionInOnePlugin()
        {
            var plugin1 = new MockPlugin("plugin1") { ThrowOnRegister = true };
            var plugin2 = new MockPlugin("plugin2");
            var plugins = new List<MockPlugin> { plugin1, plugin2 };

            _client.RegisterPlugins(plugins, _environmentMetadata, _logger);

            Assert.DoesNotContain("plugin1", _client.RegisteredPlugins);
            Assert.Contains("plugin2", _client.RegisteredPlugins);
            Assert.Single(_client.RegisteredPlugins);
        }

        [Fact]
        public void RegisterPlugins_HandlesEmptyPluginsList()
        {
            var plugins = new List<MockPlugin>();

            _client.RegisterPlugins(plugins, _environmentMetadata, _logger);

            Assert.Empty(_client.RegisteredPlugins);
        }

        [Fact]
        public void GetPluginHooks_ReturnsAllHooksFromAllPlugins()
        {
            var plugin1 = new MockPlugin("plugin1") 
            {
                Hooks = new List<MockHook> { new MockHook { Name = "hook1" } }
            };
            var plugin2 = new MockPlugin("plugin2") 
            {
                Hooks = new List<MockHook> 
                {
                    new MockHook { Name = "hook2" }, 
                    new MockHook { Name = "hook3" }
                }
            };
            var plugins = new List<MockPlugin> { plugin1, plugin2 };

            var hooks = _client.GetPluginHooks(plugins, _environmentMetadata, _logger);

            Assert.Equal(3, hooks.Count);
            Assert.Contains(hooks, h => h.Name == "hook1");
            Assert.Contains(hooks, h => h.Name == "hook2");
            Assert.Contains(hooks, h => h.Name == "hook3");
        }

        [Fact]
        public void GetPluginHooks_ContinuesAfterExceptionInOnePlugin()
        {
            var plugin1 = new MockPlugin("plugin1")
            {
                Hooks = new List<MockHook> { new MockHook { Name = "hook1" } },
                ThrowOnGetHooks = true
            };
            var plugin2 = new MockPlugin("plugin2")
            {
                Hooks = new List<MockHook> { new MockHook { Name = "hook2" } }
            };
            var plugins = new List<MockPlugin> { plugin1, plugin2 };

            var hooks = _client.GetPluginHooks(plugins, _environmentMetadata, _logger);

            Assert.Single(hooks);
            Assert.Contains(hooks, h => h.Name == "hook2");
        }

        [Fact]
        public void GetPluginHooks_HandlesNullHooksFromPlugin()
        {
            var plugin1 = new MockPlugin("plugin1")
            {
                ReturnNullHooks = true
            };
            var plugin2 = new MockPlugin("plugin2")
            {
                Hooks = new List<MockHook> { new MockHook { Name = "hook2" } }
            };
            var plugins = new List<MockPlugin> { plugin1, plugin2 };

            var hooks = _client.GetPluginHooks(plugins, _environmentMetadata, _logger);

            Assert.Single(hooks);
            Assert.Contains(hooks, h => h.Name == "hook2");
        }

        [Fact]
        public void GetPluginHooks_HandlesEmptyPluginsList()
        {
            var plugins = new List<MockPlugin>();

            var hooks = _client.GetPluginHooks(plugins, _environmentMetadata, _logger);

            Assert.Empty(hooks);
        }

        [Fact]
        public void GetPluginHooks_HandlesPluginWithNoHooks()
        {
            var plugin = new MockPlugin("plugin1"); // No hooks added
            var plugins = new List<MockPlugin> { plugin };

            var hooks = _client.GetPluginHooks(plugins, _environmentMetadata, _logger);

            Assert.Empty(hooks);
        }
    }

    // Helper class for test logging
    public static class TestLogging
    {
        public static Logger TestLogger(ITestOutputHelper testOutputHelper) =>
            Logs.ToMethod(line =>
            {
                try
                {
                    testOutputHelper.WriteLine("LOG OUTPUT >> " + line);
                }
                catch { }
            }).Logger("");
    }
} 