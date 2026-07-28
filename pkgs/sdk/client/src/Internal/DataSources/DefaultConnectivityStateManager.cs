using System;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;
using LaunchDarkly.Sdk.Client.PlatformSpecific;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    internal sealed class DefaultConnectivityStateManager : IConnectivityStateManager
    {
        public Action<bool> ConnectionChanged { get; set; }

        internal DefaultConnectivityStateManager()
        {
            UpdateConnectedStatus();
            PlatformConnectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
        }

        bool isConnected;
        bool IConnectivityStateManager.IsConnected
        {
            get { return isConnected; }
            set
            {
                isConnected = value;
            }
        }

        void Connectivity_ConnectivityChanged(object sender, EventArgs e)
        {
            UpdateConnectedStatus();
            ConnectionChanged?.Invoke(isConnected);
        }

        private void UpdateConnectedStatus()
        {
            isConnected = IsConsideredConnected(PlatformConnectivity.LdNetworkAccess);
        }

        // Unknown covers MAUI Connectivity's cold-start race; ConstrainedInternet typically
        // resolves upward. Local stays disconnected so the transition to Internet triggers
        // a fresh reconnect via ConnectivityChanged.
        internal static bool IsConsideredConnected(LdNetworkAccess access) =>
            access == LdNetworkAccess.Internet
            || access == LdNetworkAccess.Unknown
            || access == LdNetworkAccess.ConstrainedInternet;
    }
}
