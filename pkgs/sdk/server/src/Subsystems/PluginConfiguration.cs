using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Plugins;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// </summary>
    public sealed class PluginConfiguration
    {
        /// <summary>
        /// </summary>
        public IEnumerable<Plugin> Plugins { get; }

        /// <summary>
        /// </summary>
        public PluginConfiguration(IEnumerable<Plugin> plugins)
        {
            Plugins = plugins;
        }
    }
}
