using System;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;
using LaunchDarkly.Sdk.Client.PlatformSpecific;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    internal sealed class DefaultConnectivityStateManager : IConnectivityStateManager
    {
        public Action<bool> ConnectionChanged { get; set; }

        private LdNetworkAccess _currentAccess;

        internal DefaultConnectivityStateManager()
        {
            _currentAccess = PlatformConnectivity.LdNetworkAccess;
            isConnected = IsConsideredConnected(_currentAccess);
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

        void Connectivity_ConnectivityChanged(object sender, EventArgs e) =>
            HandleAccessChange(_currentAccess, PlatformConnectivity.LdNetworkAccess);

        // Updates our connectivity state for a transition from one network-access level to another
        // and notifies the listener whether/how the data source connection should change.
        //
        // Exposed as internal (rather than reading PlatformConnectivity directly) so the transition
        // behavior can be exercised in unit tests: PlatformConnectivity is a compile-time stub that
        // always reports Internet and never raises events on non-mobile target frameworks.
        internal void HandleAccessChange(LdNetworkAccess previous, LdNetworkAccess current)
        {
            _currentAccess = current;
            isConnected = IsConsideredConnected(current);

            switch (ClassifyTransition(previous, current))
            {
                case ConnectivityTransition.Connected:
                    ConnectionChanged?.Invoke(true);
                    break;
                case ConnectivityTransition.Disconnected:
                    ConnectionChanged?.Invoke(false);
                    break;
                case ConnectivityTransition.Reestablish:
                    // The collapsed isConnected bool is still true (see IsConsideredConnected), so a
                    // single ConnectionChanged(true) would no-op in ConnectionManager and leave a data
                    // source that was started prematurely -- while access was only Unknown or
                    // ConstrainedInternet and the network may not have been routable -- stuck in its own
                    // retry/backoff. Toggle network-enabled off then on to force an immediate reconnect
                    // now that we actually have Internet access.
                    ConnectionChanged?.Invoke(false);
                    ConnectionChanged?.Invoke(true);
                    break;
            }
        }

        // Unknown covers MAUI Connectivity's cold-start race; ConstrainedInternet typically
        // resolves upward. Local stays disconnected so the transition to Internet triggers
        // a fresh reconnect via ConnectivityChanged.
        internal static bool IsConsideredConnected(LdNetworkAccess access) =>
            access == LdNetworkAccess.Internet
            || access == LdNetworkAccess.Unknown
            || access == LdNetworkAccess.ConstrainedInternet;

        internal enum ConnectivityTransition
        {
            // The connectivity change requires no action on the data source connection.
            None,
            // We went from not-connected to connected: (re)establish the connection.
            Connected,
            // We went from connected to not-connected: drop the connection.
            Disconnected,
            // The connected bool did not change, but we improved up to real Internet access from a
            // weaker state that was only optimistically treated as connected: reconnect so a data
            // source that was started prematurely gets re-established promptly.
            Reestablish
        }

        // Decides what should happen to the data source connection for a transition from one
        // network-access level to another. IsConsideredConnected collapses Unknown and
        // ConstrainedInternet into the same "connected" bool as Internet, which avoids staying
        // offline at cold start but also erases the edge when the platform later resolves upward to
        // real Internet connectivity; ClassifyTransition restores that reconnect edge without
        // regressing the cold-start behavior or churning on duplicate/healthy events.
        internal static ConnectivityTransition ClassifyTransition(
            LdNetworkAccess previous, LdNetworkAccess current)
        {
            var wasConnected = IsConsideredConnected(previous);
            var nowConnected = IsConsideredConnected(current);

            if (nowConnected != wasConnected)
            {
                return nowConnected ? ConnectivityTransition.Connected : ConnectivityTransition.Disconnected;
            }

            if (nowConnected && IsImprovementToInternet(previous, current))
            {
                return ConnectivityTransition.Reestablish;
            }

            return ConnectivityTransition.None;
        }

        // True when we have just reached real Internet access from a weaker connected state that
        // IsConsideredConnected optimistically treated as connected (Unknown / ConstrainedInternet).
        // Reaching Internet from Internet (a duplicate event) or degrading away from Internet does
        // not qualify, so healthy connections are never churned.
        private static bool IsImprovementToInternet(LdNetworkAccess previous, LdNetworkAccess current) =>
            current == LdNetworkAccess.Internet && previous != LdNetworkAccess.Internet;
    }
}
