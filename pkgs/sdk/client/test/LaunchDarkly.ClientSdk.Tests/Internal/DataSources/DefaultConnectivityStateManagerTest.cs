using LaunchDarkly.Sdk.Client.PlatformSpecific;
using Xunit;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    // The connected/offline derivation (IsConsideredConnected) lives in ConnectionManager; this
    // preserves the cold-start optimism added in 5.9.3 (Unknown/ConstrainedInternet count as
    // connected) and keeps None/Local disconnected.
    public class DefaultConnectivityStateManagerTest
    {
        [Fact]
        public void Internet_IsConsideredConnected() =>
            Assert.True(ConnectionManager.IsConsideredConnected(LdNetworkAccess.Internet));

        [Fact]
        public void Unknown_IsConsideredConnected() =>
            Assert.True(ConnectionManager.IsConsideredConnected(LdNetworkAccess.Unknown));

        [Fact]
        public void None_IsNotConsideredConnected() =>
            Assert.False(ConnectionManager.IsConsideredConnected(LdNetworkAccess.None));

        [Fact]
        public void Local_IsNotConsideredConnected() =>
            Assert.False(ConnectionManager.IsConsideredConnected(LdNetworkAccess.Local));

        [Fact]
        public void ConstrainedInternet_IsConsideredConnected() =>
            Assert.True(ConnectionManager.IsConsideredConnected(LdNetworkAccess.ConstrainedInternet));
    }
}
