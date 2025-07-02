using System;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    /// <summary>
    /// Metadata about a plugin implementation.
    /// </summary>
    public sealed class PluginMetadata
    {
        /// <summary>
        /// The name of the plugin.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginMetadata"/> class with the specified name.
        /// </summary>
        /// <param name="name">The name of the plugin.</param>
        /// <exception cref="ArgumentNullException">Thrown when name is null.</exception>
        public PluginMetadata(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
} 