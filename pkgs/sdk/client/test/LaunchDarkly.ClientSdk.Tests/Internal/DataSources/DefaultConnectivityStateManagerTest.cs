using LaunchDarkly.Sdk.Client.PlatformSpecific;
using Xunit;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    public class DefaultConnectivityStateManagerTest
    {
        [Fact]
        public void Internet_IsConsideredConnected() =>
            Assert.True(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.Internet));

        [Fact]
        public void Unknown_IsConsideredConnected() =>
            Assert.True(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.Unknown));

        [Fact]
        public void None_IsNotConsideredConnected() =>
            Assert.False(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.None));

        [Fact]
        public void Local_IsNotConsideredConnected() =>
            Assert.False(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.Local));

        [Fact]
        public void ConstrainedInternet_IsConsideredConnected() =>
            Assert.True(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.ConstrainedInternet));
    }
}
