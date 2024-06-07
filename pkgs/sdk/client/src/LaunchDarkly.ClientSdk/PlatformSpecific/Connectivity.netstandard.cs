using System;
using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Client.PlatformSpecific
{
    // This code is not from MAUI Essentials, though it implements our Connectivity abstraction.
    // It is a stub that always reports that we do have network connectivity.
    //
    // Unfortunately, in .NET Standard that is the best we can do. There is (at least in 2.0) a
    // NetworkInterface.GetIsNetworkAvailable() method, but that doesn't test whether we actuually have
    // Internet access, just whether we have a network interface (i.e. if we're running a desktop app
    // on a laptop, and the wi-fi is turned off, it will still return true as long as the laptop has an
    // Ethernet card-- even if it's not plugged in).

    internal static partial class PlatformConnectivity
    {
        public static LdNetworkAccess LdNetworkAccess => LdNetworkAccess.Internet;

        public static event EventHandler ConnectivityChanged;

        public static IEnumerable<LdConnectionProfile> LdConnectionProfiles
        {
            get
            {
                yield return LdConnectionProfile.Unknown;
            }
        }
    }
}
