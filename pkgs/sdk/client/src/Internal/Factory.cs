using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Internal.DataSources;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;

namespace LaunchDarkly.Sdk.Client.Internal
{
    internal static class Factory
    {
        internal static IConnectivityStateManager CreateConnectivityStateManager(Configuration configuration)
        {
            return configuration.ConnectivityStateManager ?? new DefaultConnectivityStateManager();
        }
    }
}
