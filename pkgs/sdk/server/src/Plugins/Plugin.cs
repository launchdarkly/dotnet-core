using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Hooks;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Integrations.Plugins;

namespace LaunchDarkly.Sdk.Server.Plugins
{
    /// <summary>
    /// Abstract base class for extending SDK functionality via plugins in the server-side SDK.
    /// All provided server-side plugin implementations MUST inherit from this class.
    /// </summary>
    public abstract class Plugin : PluginBase<ILdClient, Hook> {}
}
