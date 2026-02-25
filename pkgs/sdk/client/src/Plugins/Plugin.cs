using System.Collections.Generic;
using LaunchDarkly.Sdk.Client.Hooks;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Integrations.Plugins;

namespace LaunchDarkly.Sdk.Client.Plugins
{
    /// <summary>
    /// Abstract base class for extending SDK functionality via plugins in the client-side SDK.
    /// All provided client-side plugin implementations MUST inherit from this class.
    /// </summary>
    public abstract class Plugin : PluginBase<ILdClient, Hook>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class with the specified name.
        /// </summary>
        /// <param name="name">The name of the plugin.</param>
        protected Plugin(string name)
            : base(name)
        {
        }
    }
}
