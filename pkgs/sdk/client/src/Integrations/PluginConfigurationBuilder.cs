using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Client.Plugins;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    /// <summary>
    /// PluginConfigurationBuilder is a builder for the SDK's plugin configuration.
    /// </summary>
    public sealed class PluginConfigurationBuilder
    {
        private readonly List<Plugin> _plugins;

        /// <summary>
        /// Constructs a configuration from an existing collection of plugins.
        /// </summary>
        /// <param name="plugins">optional initial collection of plugins</param>
        public PluginConfigurationBuilder(IEnumerable<Plugin> plugins = null)
        {
            _plugins = plugins is null ? new List<Plugin>() : plugins.ToList();
        }

        /// <summary>
        /// Adds a plugin to the configuration.
        /// </summary>
        /// <param name="plugin">the plugin to add</param>
        /// <returns>the builder</returns>
        public PluginConfigurationBuilder Add(Plugin plugin)
        {
            _plugins.Add(plugin);
            return this;
        }

        /// <summary>
        /// Builds the configuration.
        /// </summary>
        /// <returns>the built configuration</returns>
        public PluginConfiguration Build()
        {
            return new PluginConfiguration(_plugins.ToList());
        }
    }
}
