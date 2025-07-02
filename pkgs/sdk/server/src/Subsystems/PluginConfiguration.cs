using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Plugins;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Configuration containing plugins for the SDK.
    /// </summary>
    public sealed class PluginConfiguration
    {
        /// <summary>
        /// The collection of plugins.
        /// </summary>
        public IEnumerable<Plugin> Plugins { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class with the specified plugins.
        /// </summary>
        /// <param name="plugins">The plugins to include in this configuration.</param>
        public PluginConfiguration(IEnumerable<Plugin> plugins)
        {
            Plugins = plugins;
        }
    }
}
