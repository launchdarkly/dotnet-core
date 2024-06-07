using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Networking;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    internal static partial class PlatformConnectivity
    {
        static PlatformConnectivity()
        {
            Connectivity.Current.ConnectivityChanged += (sender, args) => ConnectivityChanged?.Invoke(sender, EventArgs.Empty);
        }

        /// <summary>
        /// Gets the current state of network access.
        /// </summary>
        public static LdNetworkAccess LdNetworkAccess => Convert(Connectivity.Current.NetworkAccess);

        /// <summary>
        /// Gets the active connectivity types for the device.
        /// </summary>
        public static IEnumerable<LdConnectionProfile> LdConnectionProfiles => Connectivity.Current.ConnectionProfiles.Distinct().Select(Convert);

        /// <summary>
        /// Occurs when network access or profile has changed.  This is just a signal and the
        /// event args are empty.  This is to avoid the need for an additional event args class
        /// container for the list of <see cref="LdConnectionProfile"/>.
        /// </summary>
        public static event EventHandler<EventArgs> ConnectivityChanged;

        private static LdConnectionProfile Convert(ConnectionProfile mauiValue) {
            switch (mauiValue) {
                case ConnectionProfile.Bluetooth:
                    return LdConnectionProfile.Bluetooth;
                case ConnectionProfile.Cellular:
                    return LdConnectionProfile.Cellular;
                case ConnectionProfile.Ethernet:
                    return LdConnectionProfile.Ethernet;
                case ConnectionProfile.WiFi:
                    return LdConnectionProfile.WiFi;
                default:
                    return LdConnectionProfile.Unknown;
            }
        }

        private static LdNetworkAccess Convert(NetworkAccess mauiValue)
        {
            switch (mauiValue)
            {
                case NetworkAccess.None:
                    return LdNetworkAccess.None;
                case NetworkAccess.Local:
                    return LdNetworkAccess.Local;
                case NetworkAccess.ConstrainedInternet:
                    return LdNetworkAccess.ConstrainedInternet;
                case NetworkAccess.Internet:
                    return LdNetworkAccess.Internet;
                default:
                    return LdNetworkAccess.Unknown;
            }
        }
    }
}
