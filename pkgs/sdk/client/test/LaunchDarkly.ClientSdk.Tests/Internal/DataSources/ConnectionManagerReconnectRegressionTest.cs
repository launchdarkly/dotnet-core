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

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    // Regression coverage for the connectivity-reconnect bug introduced by PR #323 (shipped 5.9.3).
    //
    // #323 made Unknown/ConstrainedInternet count as "connected" (IsConsideredConnected). That avoids
    // staying offline at cold start, but on a mobile resume the platform reports Unknown then Internet
    // -- both now "connected" -- so the Unknown->Internet transition produced no change in the derived
    // connected/offline decision. A data source started prematurely during the Unknown phase (when the
    // network was not yet routable) was therefore never re-established, and the SDK stayed offline
    // until the data source's own backoff recovered ("several seconds").
    //
    // Design under test: the connectivity layer reports the granular LdNetworkAccess, and
    // ConnectionManager.SetNetworkAccess derives the connected/offline decision AND, on a recovery up
    // to real Internet from a weaker-but-considered-connected level (Unknown/ConstrainedInternet),
    // re-establishes the data source -- health-gated so a healthy (Valid) stream is never rebuilt on a
    // nominal flap. No false->true toggle, no fabricated NetworkUnavailable blip.
    public class ConnectionManagerReconnectRegressionTest
    {
        private readonly Logger _log = Logs.None.Logger("");

        // A data source we fully control: records Start/Dispose counts and either stalls forever
        // (never reaches Valid) or succeeds immediately (reports Valid via the sink, like a real one).
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
                    return new TaskCompletionSource<bool>().Task; // never signals success/Valid
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

            // stallForBuildIndex is given the 0-based build index and returns whether that data source
            // should stall. Lets a test stall only the first (premature) connect.
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

        // Synchronous update sink that records the full status history and exposes the current status,
        // mirroring the relevant DataSourceUpdateSinkImpl behavior: Init implies Valid, and an
        // Interrupted reported before the first success stays Initializing. Synchronous so the test
        // can assert the exact status sequence (and the absence of a NetworkUnavailable blip).
        private sealed class StatusRecordingSink : IDataSourceUpdateSink
        {
            public readonly List<DataSourceState> History = new List<DataSourceState>();
            public DataSourceStatus Current { get; private set; } =
                new DataSourceStatus { State = DataSourceState.Initializing };

            public void Init(Context context, FullDataSet data) => Record(DataSourceState.Valid);

            public void Upsert(Context context, string key, ItemDescriptor data) { }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                var effective =
                    (newState == DataSourceState.Interrupted && Current.State == DataSourceState.Initializing)
                        ? DataSourceState.Initializing
                        : newState;
                Record(effective);
            }

            private void Record(DataSourceState state)
            {
                History.Add(state);
                Current = new DataSourceStatus { State = state };
            }
        }

        private ConnectionManager MakeConnectionManager(
            RecordingDataSourceFactory factory, StatusRecordingSink sink) =>
            new ConnectionManager(
                TestUtil.SimpleContext,
                factory,
                sink,
                () => sink.Current,          // health reader (status tracking, not _initialized)
                new MockEventProcessor(),
                null,                        // diagnosticDisabler (ConnectionManager null-checks it)
                true,                        // enableBackgroundUpdating
                Context.New("userkey"),
                _log);

        // ------------------------------------------------------------------
        // REGRESSION TEST: Unknown -> Internet with a stalled premature connect must recover.
        //
        // Red before the fix: SetNetworkEnabled(true) -> SetNetworkEnabled(true) no-oped, the stalled
        // source was never replaced, status never left Initializing. Green after: reaching Internet
        // re-establishes a healthy data source and status reaches Valid -- with NO NetworkUnavailable
        // blip (the old toggle's artifact is gone).
        // ------------------------------------------------------------------
        [Fact]
        public void UnknownThenInternet_reestablishesHealthyDataSourceAndReachesValid()
        {
            var factory = new RecordingDataSourceFactory(buildIndex => buildIndex == 0); // first stalls
            var sink = new StatusRecordingSink();
            using var cm = MakeConnectionManager(factory, sink);

            // Cold start while access is still Unknown: a connect begins and stalls.
            cm.SetNetworkAccess(LdNetworkAccess.Unknown); // before Start: sets enabled, no build yet
            cm.Start();                                   // builds DS#1 during Unknown; it stalls
            Assert.Equal(1, factory.BuildCount);
            Assert.True(factory.Built[0].Stalls);
            Assert.Equal(new[] { DataSourceState.Initializing }, sink.History);

            // Platform resolves up to real Internet: must reconnect to a healthy data source.
            cm.SetNetworkAccess(LdNetworkAccess.Internet);

            Assert.Equal(2, factory.BuildCount);              // a fresh data source was built ...
            Assert.Equal(1, factory.Built[0].DisposeCount);   // ... the stalled one was torn down ...
            Assert.False(factory.Built[1].Stalls);
            Assert.Equal(1, factory.Built[1].StartCount);     // ... and started.
            Assert.Equal(DataSourceState.Valid, sink.Current.State); // recovered to Valid

            // No fabricated offline: the recovery path never emitted NetworkUnavailable.
            Assert.DoesNotContain(DataSourceState.NetworkUnavailable, sink.History);
            Assert.Equal(
                new[] { DataSourceState.Initializing, DataSourceState.Initializing, DataSourceState.Valid },
                sink.History);
        }

        // ------------------------------------------------------------------
        // NO-CHURN: a healthy (Valid) connection that sees a nominal flap
        // (Internet -> ConstrainedInternet -> Internet) is NOT torn down or rebuilt, and status
        // never flips to NetworkUnavailable. This is the anti-churn health gate.
        // ------------------------------------------------------------------
        [Fact]
        public void HealthyConnection_nominalFlap_isNotRebuilt()
        {
            var factory = new RecordingDataSourceFactory(_ => false); // always healthy
            var sink = new StatusRecordingSink();
            using var cm = MakeConnectionManager(factory, sink);

            cm.SetNetworkAccess(LdNetworkAccess.Internet);
            cm.Start(); // builds DS#1, healthy -> Valid
            Assert.Equal(1, factory.BuildCount);
            Assert.Equal(DataSourceState.Valid, sink.Current.State);
            var historyAfterConnect = new List<DataSourceState>(sink.History);

            // A nominal flap down to ConstrainedInternet and back up to Internet.
            cm.SetNetworkAccess(LdNetworkAccess.ConstrainedInternet); // still "connected": no-op
            cm.SetNetworkAccess(LdNetworkAccess.Internet);            // recovery edge, but gated by Valid

            Assert.Equal(1, factory.BuildCount);               // never rebuilt
            Assert.Equal(0, factory.Built[0].DisposeCount);    // never torn down
            Assert.Equal(DataSourceState.Valid, sink.Current.State);
            Assert.DoesNotContain(DataSourceState.NetworkUnavailable, sink.History);
            Assert.Equal(historyAfterConnect, sink.History);   // no status churn at all
        }

        // ------------------------------------------------------------------
        // Normal edges still work: a true disconnect goes offline (NetworkUnavailable), and the
        // following reconnect rebuilds the data source back to Valid.
        // ------------------------------------------------------------------
        [Fact]
        public void DisconnectThenReconnect_goesOfflineThenRebuilds()
        {
            var factory = new RecordingDataSourceFactory(_ => false); // always healthy
            var sink = new StatusRecordingSink();
            using var cm = MakeConnectionManager(factory, sink);

            cm.SetNetworkAccess(LdNetworkAccess.Internet);
            cm.Start(); // DS#1 -> Valid
            Assert.Equal(1, factory.BuildCount);

            cm.SetNetworkAccess(LdNetworkAccess.None); // real disconnect
            Assert.Equal(1, factory.Built[0].DisposeCount);
            Assert.Equal(DataSourceState.NetworkUnavailable, sink.Current.State);

            cm.SetNetworkAccess(LdNetworkAccess.Internet); // reconnect
            Assert.Equal(2, factory.BuildCount);
            Assert.Equal(DataSourceState.Valid, sink.Current.State);
        }

        // ------------------------------------------------------------------
        // The recovery predicate: fires only for a rise to real Internet from a weaker
        // considered-connected level; never on duplicate Internet, rises from disconnected (handled by
        // the normal connected/offline edge), or degradations.
        // ------------------------------------------------------------------
        [Theory]
        [InlineData((int)LdNetworkAccess.Unknown, (int)LdNetworkAccess.Internet, true)]
        [InlineData((int)LdNetworkAccess.ConstrainedInternet, (int)LdNetworkAccess.Internet, true)]
        [InlineData((int)LdNetworkAccess.Internet, (int)LdNetworkAccess.Internet, false)]
        [InlineData((int)LdNetworkAccess.None, (int)LdNetworkAccess.Internet, false)]
        [InlineData((int)LdNetworkAccess.Local, (int)LdNetworkAccess.Internet, false)]
        [InlineData((int)LdNetworkAccess.Unknown, (int)LdNetworkAccess.ConstrainedInternet, false)]
        [InlineData((int)LdNetworkAccess.Internet, (int)LdNetworkAccess.Unknown, false)]
        public void IsRecoveryToInternet_onlyOnRiseToInternetFromWeakerConnected(
            int previous, int current, bool expected) =>
            Assert.Equal(
                expected,
                ConnectionManager.IsRecoveryToInternet((LdNetworkAccess)previous, (LdNetworkAccess)current));

        // ------------------------------------------------------------------
        // Ties to #323's exact mapping, now owned by ConnectionManager: Unknown and
        // ConstrainedInternet collapse into "connected" with Internet (cold-start optimism preserved);
        // None/Local remain disconnected. (DefaultConnectivityStateManagerTest also covers this.)
        // ------------------------------------------------------------------
        [Fact]
        public void IsConsideredConnected_collapsesUnknownAndConstrainedIntoConnected()
        {
            Assert.True(ConnectionManager.IsConsideredConnected(LdNetworkAccess.Internet));
            Assert.True(ConnectionManager.IsConsideredConnected(LdNetworkAccess.Unknown));
            Assert.True(ConnectionManager.IsConsideredConnected(LdNetworkAccess.ConstrainedInternet));
            Assert.False(ConnectionManager.IsConsideredConnected(LdNetworkAccess.None));
            Assert.False(ConnectionManager.IsConsideredConnected(LdNetworkAccess.Local));
        }
    }
}
