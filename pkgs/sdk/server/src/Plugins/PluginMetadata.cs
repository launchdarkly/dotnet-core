using System;

namespace LaunchDarkly.Sdk.Server.Plugins
{
    /// <summary>
    /// </summary>
    public sealed class PluginMetadata
    {
        /// <summary>
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginMetadata"/> class with the specified name.
        /// </summary>
        public PluginMetadata(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
