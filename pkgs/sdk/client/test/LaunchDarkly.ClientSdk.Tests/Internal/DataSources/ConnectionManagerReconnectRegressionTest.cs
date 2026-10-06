using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.PlatformSpecific;
using LaunchDarkly.Sdk.Client.Subsystems;
using Xunit;

// The Mock* helpers (MockEventProcessor, etc.) and TestUtil live in the
// LaunchDarkly.Sdk.Client namespace (see MockComponents.cs / TestUtil.cs).
using LaunchDarkly.Sdk.Client;

using Transition = LaunchDarkly.Sdk.Client.Internal.DataSources
    .DefaultConnectivityStateManager.ConnectivityTransition;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    // Regression coverage for the connectivity-reconnect bug introduced by PR #323 (shipped 5.9.3).
    //
    // #323 changed DefaultConnectivityStateManager so "is connected" went from
    //   access == Internet
    // to
    //   IsConsideredConnected(access) == (Internet || Unknown || ConstrainedInternet).
    //
    // On a mobile resume the platform typically reports Unknown first, then Internet. Post-#323 both
    // map to isConnected=true, so the Unknown->Internet transition produced no change in the collapsed
    // bool. ConnectionManager.SetNetworkEnabled(bool) early-returns when the value is unchanged and
    // OpenOrCloseConnectionIfNecessary(false) does not rebuild an existing data source, so a data
    // source that was started prematurely while access was still Unknown (and the network not yet
    // routable) was never re-established -- the SDK stayed offline until the data source's own backoff
    // eventually recovered ("several seconds").
    //
    // The fix restores the reconnect edge in DefaultConnectivityStateManager: reaching real Internet
    // access from a weaker optimistically-"connected" state (Unknown/ConstrainedInternet) now forces a
    // reconnect, while the cold-start optimism of #323 and churn-free behavior on duplicate/healthy
    // events are preserved.
    public class ConnectionManagerReconnectRegressionTest
    {
        private readonly Logger _log = Logs.None.Logger("");

        // A data source we fully control: it records Start/Dispose counts, and either stalls forever
        // (simulating a premature connect that never reaches Valid) or succeeds immediately
        // (simulating a connect made when Internet is actually available).
        private sealed class RecordingDataSource : IDataSource
        {
            private readonly IDataSourceUpdateSink _sink;
            public readonly bool Stalls;
            public int StartCount;
            public int DisposeCount;

            public RecordingDataSource(bool stalls, IDataSourceUpdateSink sink)
            {
                Stalls = stalls;
                _sink = sink;
            }

            public bool Initialized => false;

            public Task<bool> Start()
            {
                Interlocked.Increment(ref StartCount);
                if (Stalls)
                {
                    // Never signals success/Valid.
                    return new TaskCompletionSource<bool>().Task;
                }
                _sink?.UpdateStatus(DataSourceState.Valid, null);
                return Task.FromResult(true);
            }

            public void Dispose() => Interlocked.Increment(ref DisposeCount);
        }

        private sealed class RecordingDataSourceFactory : IComponentConfigurer<IDataSource>
        {
            private readonly Func<int, bool> _stallForBuildIndex;
            public int BuildCount;
            public readonly List<RecordingDataSource> Built = new List<RecordingDataSource>();

            // stallForBuildIndex is given the 0-based index of the build and returns whether that
            // data source should stall. Lets a test stall only the first (premature) connect.
            public RecordingDataSourceFactory(Func<int, bool> stallForBuildIndex) =>
                _stallForBuildIndex = stallForBuildIndex;

            public IDataSource Build(LdClientContext context)
            {
                var index = BuildCount;
                Interlocked.Increment(ref BuildCount);
                var ds = new RecordingDataSource(_stallForBuildIndex(index), context.DataSourceUpdateSink);
                lock (Built)
                {
                    Built.Add(ds);
                }
                return ds;
            }
        }

        private ConnectionManager MakeConnectionManager(
            RecordingDataSourceFactory factory, IDataSourceUpdateSink sink) =>
            new ConnectionManager(
                TestUtil.SimpleContext,
                factory,
                sink,
                new MockEventProcessor(),
                null,                        // diagnosticDisabler (ConnectionManager null-checks it)
                true,                        // enableBackgroundUpdating
                Context.New("userkey"),
                _log);

        // ------------------------------------------------------------------
        // REGRESSION TEST: the Unknown->Internet transition must recover.
        //
        // Drives the real fixed chain end-to-end, wired exactly as LdClient wires it
        // (DefaultConnectivityStateManager.ConnectionChanged -> ConnectionManager.SetNetworkEnabled),
        // and asserts the DESIRED behavior: after the platform resolves up to Internet, a fresh healthy
        // data source is established and DataSourceStatus reaches Valid.
        //
        // This is red before the fix: pre-fix, the Unknown->Internet transition produces no reconnect
        // signal, the stalled data source is never replaced, and status never leaves Initializing.
        //
        // PlatformConnectivity is a compile-time stub on this (net8.0) TFM, so connectivity levels are
        // fed via the real DefaultConnectivityStateManager.HandleAccessChange decision method rather
        // than through the (undrivable) static event source.
        // ------------------------------------------------------------------
        [Fact]
        public void UnknownThenInternet_reestablishesHealthyDataSourceAndReachesValid()
        {
            // The first (premature) data source stalls; any later one is healthy.
            var factory = new RecordingDataSourceFactory(buildIndex => buildIndex == 0);
            var sink = new MockDataSourceUpdateSink();
            using var cm = MakeConnectionManager(factory, sink);

            var connectivity = new DefaultConnectivityStateManager
            {
                ConnectionChanged = enabled => { _ = cm.SetNetworkEnabled(enabled); }
            };

            cm.Start();
            Assert.Equal(DataSourceState.NetworkUnavailable, sink.ExpectStatusUpdate().State);

            // Cold start / first connected signal arrives while access is still Unknown: a connect
            // attempt begins and stalls (never reaches Valid).
            connectivity.HandleAccessChange(LdNetworkAccess.None, LdNetworkAccess.Unknown);
            Assert.Equal(DataSourceState.Initializing, sink.ExpectStatusUpdate().State);
            Assert.Equal(1, factory.BuildCount);
            Assert.True(factory.Built[0].Stalls);

            // The platform resolves up to real Internet. The SDK MUST reconnect and establish a
            // healthy data source rather than remaining stuck on the stalled one.
            connectivity.HandleAccessChange(LdNetworkAccess.Unknown, LdNetworkAccess.Internet);

            // A fresh, healthy data source was built and started ...
            Assert.Equal(2, factory.BuildCount);
            Assert.Equal(1, factory.Built[0].DisposeCount);  // the stalled source was torn down
            Assert.False(factory.Built[1].Stalls);
            Assert.Equal(1, factory.Built[1].StartCount);

            // ... and the connection recovered to Valid. The brief NetworkUnavailable is the
            // force-reconnect toggle (see the design trade-off noted in the PR).
            Assert.Equal(DataSourceState.NetworkUnavailable, sink.ExpectStatusUpdate().State);
            Assert.Equal(DataSourceState.Initializing, sink.ExpectStatusUpdate().State);
            Assert.Equal(DataSourceState.Valid, sink.ExpectStatusUpdate().State);
            sink.ExpectNoMoreActions();
        }

        // ------------------------------------------------------------------
        // Control: the pre-#323 mapping, where Unknown->false and Internet->true. The false->true
        // edge on the Internet event builds+starts a data source. Confirms the ConnectionManager
        // reconnect path itself is healthy; the regression is purely about the lost edge.
        // ------------------------------------------------------------------
        [Fact]
        public async Task Control_FalseThenTrue_reconnectsOnConnectedEdge()
        {
            var factory = new RecordingDataSourceFactory(_ => false); // always healthy
            var sink = new MockDataSourceUpdateSink();
            using var cm = MakeConnectionManager(factory, sink);

            cm.Start();
            Assert.Equal(DataSourceState.NetworkUnavailable, sink.ExpectStatusUpdate().State);

            _ = cm.SetNetworkEnabled(false); // Unknown mapped to "not connected" pre-#323: no-op
            Assert.Equal(0, factory.BuildCount);

            var result = await cm.SetNetworkEnabled(true); // the preserved false->true edge

            Assert.True(result);
            Assert.Equal(1, factory.BuildCount);
            Assert.Equal(1, factory.Built[0].StartCount);
            Assert.Equal(DataSourceState.Initializing, sink.ExpectStatusUpdate().State);
            Assert.Equal(DataSourceState.Valid, sink.ExpectStatusUpdate().State);
        }

        // ------------------------------------------------------------------
        // The connectivity decision table: restores the reconnect edge #323 erased without churning
        // on duplicate/healthy events or regressing cold-start optimism.
        // ------------------------------------------------------------------
        // Parameters are the enum underlying ints because the enums are internal and an xUnit
        // [Theory] method must be public (public signatures cannot expose internal types).
        [Theory]
        // Recovery edges erased by #323 -> must reconnect.
        [InlineData((int)LdNetworkAccess.Unknown, (int)LdNetworkAccess.Internet, (int)Transition.Reestablish)]
        [InlineData((int)LdNetworkAccess.ConstrainedInternet, (int)LdNetworkAccess.Internet, (int)Transition.Reestablish)]
        // Normal connected / disconnected edges.
        [InlineData((int)LdNetworkAccess.None, (int)LdNetworkAccess.Internet, (int)Transition.Connected)]
        [InlineData((int)LdNetworkAccess.Local, (int)LdNetworkAccess.Internet, (int)Transition.Connected)]
        [InlineData((int)LdNetworkAccess.None, (int)LdNetworkAccess.Unknown, (int)Transition.Connected)]
        [InlineData((int)LdNetworkAccess.Internet, (int)LdNetworkAccess.None, (int)Transition.Disconnected)]
        [InlineData((int)LdNetworkAccess.Unknown, (int)LdNetworkAccess.None, (int)Transition.Disconnected)]
        // No churn: duplicate events, healthy Internet, degradations within "connected", both-offline.
        [InlineData((int)LdNetworkAccess.Internet, (int)LdNetworkAccess.Internet, (int)Transition.None)]
        [InlineData((int)LdNetworkAccess.Unknown, (int)LdNetworkAccess.Unknown, (int)Transition.None)]
        [InlineData((int)LdNetworkAccess.Internet, (int)LdNetworkAccess.Unknown, (int)Transition.None)]
        [InlineData((int)LdNetworkAccess.None, (int)LdNetworkAccess.Local, (int)Transition.None)]
        public void ClassifyTransition_restoresRecoveryEdgeWithoutChurn(
            int previous, int current, int expected) =>
            Assert.Equal(
                (Transition)expected,
                DefaultConnectivityStateManager.ClassifyTransition(
                    (LdNetworkAccess)previous, (LdNetworkAccess)current));

        // ------------------------------------------------------------------
        // Ties the behavior to #323's exact change: the edge collapse in IsConsideredConnected.
        // Unknown and ConstrainedInternet map to the same "connected" value as Internet (preserving
        // #323's cold-start optimism); None/Local remain disconnected.
        // (DefaultConnectivityStateManagerTest also covers this mapping directly.)
        // ------------------------------------------------------------------
        [Fact]
        public void IsConsideredConnected_collapsesUnknownAndConstrainedIntoConnected()
        {
            Assert.True(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.Internet));
            Assert.True(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.Unknown));
            Assert.True(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.ConstrainedInternet));
            Assert.False(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.None));
            Assert.False(DefaultConnectivityStateManager.IsConsideredConnected(LdNetworkAccess.Local));
        }
    }
}
