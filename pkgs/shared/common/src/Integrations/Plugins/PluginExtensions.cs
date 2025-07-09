using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    /// <summary>
    /// Extension methods for plugin registration and hook collection.
    /// </summary>
    public static class PluginExtensions
    {
        /// <summary>
        /// Registers all plugins with the client and environment metadata.
        /// </summary>
        /// <typeparam name="TClient">The client type (e.g., ILdClient)</typeparam>
        /// <typeparam name="THook">The hook type (e.g., Hook)</typeparam>
        /// <param name="client">The client instance to register plugins with</param>
        /// <param name="plugins">The collection of plugins to register</param>
        /// <param name="environmentMetadata">Metadata about the environment</param>
        /// <param name="logger">Logger for error reporting</param>
        /// <remarks>
        /// This method iterates through each plugin in the collection and calls its `Register` method
        /// to initialize it with the client and environment metadata. It logs any exceptions that occur during
        /// the registration process, allowing the client to continue functioning even if some plugins fail to register.
        /// </remarks>
        public static void RegisterPlugins<TClient, THook>(
            this TClient client,
            IEnumerable<PluginBase<TClient, THook>> plugins,
            EnvironmentMetadata environmentMetadata,
            Logger logger)
        {
            foreach (var plugin in plugins)
            {
                try
                {
                    plugin.Register(client, environmentMetadata);
                }
                catch (Exception ex)
                {
                    logger.Error("Error registering plugin {0}: {1}",
                        plugin.Metadata.Name ?? "unknown", ex);
                }
            }
        }

        /// <summary>
        /// Retrieves all hooks from the specified plugins.
        /// </summary>
        /// <typeparam name="TClient">The client type</typeparam>
        /// <typeparam name="THook">The hook type</typeparam>
        /// <param name="client">The client instance to register plugins with</param>
        /// <param name="plugins">The collection of plugins</param>
        /// <param name="environmentMetadata">Metadata about the environment</param>
        /// <param name="logger">Logger for error reporting</param>
        /// <returns>A list of hooks from all plugins</returns>
        /// <remarks>
        /// This method iterates through each plugin in the collection and calls its `GetHooks` method
        /// to retrieve any hooks the plugin provides. It logs any exceptions that occur during
        /// the hook retrieval process and continues processing remaining plugins.
        /// </remarks>
        public static List<THook> GetPluginHooks<TClient, THook>(
            this TClient client,
            IEnumerable<PluginBase<TClient, THook>> plugins,
            EnvironmentMetadata environmentMetadata,
            Logger logger)
        {
            var allHooks = new List<THook>();
            foreach (var plugin in plugins)
            {
                try
                {
                    var pluginHooks = plugin.GetHooks(environmentMetadata);
                    if (pluginHooks != null)
                    {
                        allHooks.AddRange(pluginHooks);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("Error getting hooks from plugin {0}: {1}",
                        plugin.Metadata.Name ?? "unknown", ex);
                }
            }
            return allHooks;
        }
    }
}