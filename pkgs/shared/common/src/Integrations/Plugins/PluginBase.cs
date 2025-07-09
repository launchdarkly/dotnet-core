using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    /// <summary>
    /// Abstract base class for extending SDK functionality via plugins.
    /// Consumers should provide specific implementations of this class with the appropariate
    /// client and hook types for their use case.
    /// This class includes default implementations for optional methods, allowing
    /// LaunchDarkly to expand the list of plugin methods without breaking customer integrations.
    /// Plugins provide an interface which allows for initialization, access to credentials,
    /// and hook registration in a single interface.
    /// </summary>
    /// <typeparam name="TClient">The type of the LaunchDarkly client (e.g., ILdClient)</typeparam>
    /// <typeparam name="THook">The type of hooks used by this plugin (e.g., Hook)</typeparam>
    public abstract class PluginBase<TClient, THook>
    {
        /// <summary>
        /// Get metadata about the plugin implementation.
        /// </summary>
        /// <returns>Metadata describing this plugin</returns>
        public PluginMetadata Metadata { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginBase{TClient, THook}"/> class with the specified name.
        /// </summary>
        /// <param name="name">The name of the plugin.</param>
        public PluginBase(string name)
        {
            Metadata = new PluginMetadata(name);
        }

        /// <summary>
        /// Registers the plugin with the specified LaunchDarkly client and environment metadata.
        /// </summary>
        /// <param name="client">An instance of the LaunchDarkly client to register the plugin with.</param>
        /// <param name="metadata">Metadata about the environment.</param>
        public abstract void Register(TClient client, EnvironmentMetadata metadata);

        /// <summary>
        /// Returns a list of hooks to be registered for the plugin, based on the provided environment metadata.
        /// </summary>
        /// <param name="metadata">Metadata about the environment.</param>
        /// <returns>A list of hook instances to be registered.</returns>
        public virtual IList<THook> GetHooks(EnvironmentMetadata metadata)
        {
            return new List<THook>();
        }
    }
}