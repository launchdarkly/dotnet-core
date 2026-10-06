using System;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;
using LaunchDarkly.Sdk.Client.PlatformSpecific;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    internal sealed class DefaultConnectivityStateManager : IConnectivityStateManager
    {
        public Action<LdNetworkAccess> ConnectionChanged { get; set; }

        private LdNetworkAccess _currentAccess;

        internal DefaultConnectivityStateManager()
        {
            _currentAccess = PlatformConnectivity.LdNetworkAccess;
            PlatformConnectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
        }

        public LdNetworkAccess NetworkAccess => _currentAccess;

        // Report the granular access level on every change and let the connection layer decide what
        // to do with it. This keeps the network-access semantics (what counts as connected, and how
        // to react to an improvement) in one place -- ConnectionManager -- instead of being flattened
        // to a bool here.
        void Connectivity_ConnectivityChanged(object sender, EventArgs e)
        {
            _currentAccess = PlatformConnectivity.LdNetworkAccess;
            ConnectionChanged?.Invoke(_currentAccess);
        }
    }
}
