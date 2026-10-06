using System;
using LaunchDarkly.Sdk.Client.PlatformSpecific;

namespace LaunchDarkly.Sdk.Client.Internal.Interfaces
{
    internal interface IConnectivityStateManager
    {
        // The current network access level reported by the platform.
        LdNetworkAccess NetworkAccess { get; }

        // Invoked with the new network access level whenever platform connectivity changes.
        // The connection layer decides what the access level means for the data source.
        Action<LdNetworkAccess> ConnectionChanged { get; set; }
    }
}
