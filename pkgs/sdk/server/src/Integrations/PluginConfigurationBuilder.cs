using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Plugins;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// PluginConfigurationBuilder is a builder for the SDK's plugin configuration.
    /// </summary>
    public sealed class PluginConfigurationBuilder
    {
        private readonly List<Plugin> _plugins;

        /// <summary>
        /// Constructs a configuration representing no plugins by default.
        /// </summary>
        public PluginConfigurationBuilder() : this(new List<Plugin>())
        {
        }

        /// <summary>
        /// Constructs a configuration from an existing collection of plugins.
        /// </summary>
        public PluginConfigurationBuilder(IEnumerable<Plugin> plugins)
        {
            _plugins = plugins.ToList();
        }

        /// <summary>
        /// Adds a plugin to the configuration.
        /// </summary>
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
            return new PluginConfiguration(_plugins);
        }
    }
}
