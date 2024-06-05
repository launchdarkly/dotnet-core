using System;

namespace LaunchDarkly.Sdk.Client.Internal.Interfaces
{
    internal interface IConnectivityStateManager
    {
        bool IsConnected { get; set; }
        Action<bool> ConnectionChanged { get; set; }
    }
}
