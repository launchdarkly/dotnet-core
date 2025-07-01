using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Hooks;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.Sdk.Server.Plugins
{
    /// <summary>
    /// Abstract base class for extending SDK functionality via plugins.
    /// All provided plugin implementations MUST inherit from this class.
    /// This class includes default implementations for optional methods, allowing
    /// LaunchDarkly to expand the list of plugin methods without breaking customer integrations.
    /// Plugins provide an interface which allows for initialization, access to credentials,
    /// and hook registration in a single interface.
    /// </summary>
    public abstract class Plugin
    {
        /// <summary>
        /// Get metadata about the plugin implementation.
        /// </summary>
        public abstract PluginMetadata GetMetadata();

        /// <summary>
        /// Registers the plugin with the specified LaunchDarkly client and environment metadata.
        /// </summary>
        /// <param name="client">An instance of the LaunchDarkly client (ILdClient) to register the plugin with.</param>
        /// <param name="metadata">Metadata about the environment.</param>
        public abstract void Register(ILdClient client, EnvironmentMetadata metadata);

        /// <summary>
        /// Returns a list of hooks to be registered for the plugin, based on the provided environment metadata.
        /// </summary>
        /// <param name="metadata">Metadata about the environment.</param>
        /// <returns>A list of <see cref="Hook"/> instances to be registered.</returns>
        public virtual IList<Hook> GetHooks(EnvironmentMetadata metadata)
        {
            return new List<Hook>();
        }
    }
}
