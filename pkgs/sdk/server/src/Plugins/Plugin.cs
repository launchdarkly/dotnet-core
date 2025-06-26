using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Hooks;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.Sdk.Server.Plugins
{
    /// <summary>
    /// </summary>
    /// <remarks>
    /// </remarks>
    public abstract class Plugin
    {
        /// <summary>
        /// </summary>
        public abstract PluginMetadata GetMetadata();

        /// <summary>
        /// </summary>
        public abstract void Register(ILdClient client, EnvironmentMetadata metadata);

        /// <summary>
        /// </summary>
        /// <remarks>
        /// </remarks>
        public virtual IList<Hook> GetHooks(EnvironmentMetadata metadata)
        {
            return new List<Hook>();
        }
    }
}
