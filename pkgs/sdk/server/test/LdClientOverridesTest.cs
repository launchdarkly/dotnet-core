using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Json;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.Evaluation;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server
{
    // Client-level behavior of the override layer: precedence over LaunchDarkly data, serving
    // overrides before the client has LaunchDarkly data, the all-flags state, flag change
    // notifications, and the source lifecycle.
    public class LdClientOverridesTest : BaseTest
    {
        private static readonly Context context = Context.New("userkey");

        public LdClientOverridesTest(ITestOutputHelper testOutput) : base(testOutput) { }

        internal static FullDataSet<ItemDescriptor> FlagsOnly(params FeatureFlag[] flags)
        {
            var builder = new DataSetBuilder();
            foreach (var flag in flags)
            {
                builder.Flags(flag);
            }
            return builder.Build();
        }

        internal static FeatureFlag SingleValueFlag(string key, LdValue value) =>
            new FeatureFlagBuilder(key).On(true).FallthroughVariation(0).Variations(value).Build();

        // A client whose data system can never obtain LaunchDarkly data.
        private LdClient MakeUninitializedClient(TestOverrideSource source, IEventProcessor events = null)
        {
            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceThatNeverStarts())
                    .Overrides(source));
            if (events != null)
            {
                config.Events(events.AsSingletonFactory());
            }
            var client = new LdClient(config.Build());
            Assert.False(client.Initialized);
            return client;
        }

        // A client that has initialized with the given LaunchDarkly data.
        private LdClient MakeInitializedClient(FullDataSet<ItemDescriptor> launchDarklyData, TestOverrideSource source,
            IEventProcessor events = null)
        {
            var config = BasicConfig()
                .StartWaitTime(TimeSpan.FromSeconds(5))
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceWithData(launchDarklyData))
                    .Overrides(source));
            if (events != null)
            {
                config.Events(events.AsSingletonFactory());
            }
            var client = new LdClient(config.Build());
            Assert.True(client.Initialized);
            return client;
        }

        [Fact]
        public void OverrideIsServedWhenClientIsNotInitialized()
        {
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
            using (var client = MakeUninitializedClient(source))
            {
                var detail = client.BoolVariationDetail("overridden-flag", context, false);
                Assert.True(detail.Value);
                Assert.Equal(0, detail.VariationIndex);
                Assert.Equal(EvaluationReasonKind.Fallthrough, detail.Reason.Kind);
                Assert.True(detail.Reason.OverrideAffected);
            }
        }

        [Fact]
        public void NonOverriddenFlagStillShortCircuitsWhenClientIsNotInitialized()
        {
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
            using (var client = MakeUninitializedClient(source))
            {
                var detail = client.BoolVariationDetail("other-flag", context, false);
                Assert.False(detail.Value);
                Assert.Null(detail.VariationIndex);
                Assert.Equal(EvaluationReason.ErrorReason(EvaluationErrorKind.ClientNotReady), detail.Reason);
                Assert.False(detail.Reason.OverrideAffected);
            }
        }

        [Fact]
        public void OverrideRemovalRestoresShortCircuit()
        {
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
            using (var client = MakeUninitializedClient(source))
            {
                Assert.True(client.BoolVariation("overridden-flag", context, false));

                source.SetOverrides(FullDataSet<ItemDescriptor>.Empty());

                var detail = client.BoolVariationDetail("overridden-flag", context, false);
                Assert.False(detail.Value);
                Assert.Equal(EvaluationReason.ErrorReason(EvaluationErrorKind.ClientNotReady), detail.Reason);
            }
        }

        [Fact]
        public void OverrideAddedLaterIsServedWithoutRestart()
        {
            var source = new TestOverrideSource();
            using (var client = MakeUninitializedClient(source))
            {
                Assert.False(client.BoolVariation("overridden-flag", context, false));

                source.SetOverrides(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));

                Assert.True(client.BoolVariation("overridden-flag", context, false));
            }
        }

        [Fact]
        public void OverridesDoNotAffectInitializationOrDataSourceStatus()
        {
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
            using (var client = MakeUninitializedClient(source))
            {
                Assert.False(client.Initialized);
                Assert.Equal(DataSourceState.Initializing, client.DataSourceStatusProvider.Status.State);
            }
        }

        [Fact]
        public void OverrideTakesPrecedenceOverLaunchDarklyData()
        {
            var ldFlag = new FeatureFlagBuilder("flag").Version(100).OffWithValue(LdValue.Of("ld-value")).Build();
            var ldOther = new FeatureFlagBuilder("other").Version(100).OffWithValue(LdValue.Of("other-value")).Build();
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("flag", LdValue.Of("override-value"))));
            using (var client = MakeInitializedClient(FlagsOnly(ldFlag, ldOther), source))
            {
                var detail = client.StringVariationDetail("flag", context, "default");
                Assert.Equal("override-value", detail.Value);
                Assert.True(detail.Reason.OverrideAffected);

                var otherDetail = client.StringVariationDetail("other", context, "default");
                Assert.Equal("other-value", otherDetail.Value);
                Assert.False(otherDetail.Reason.OverrideAffected);

                // Removing the override returns the flag to LaunchDarkly data.
                source.SetOverrides(FullDataSet<ItemDescriptor>.Empty());
                detail = client.StringVariationDetail("flag", context, "default");
                Assert.Equal("ld-value", detail.Value);
                Assert.False(detail.Reason.OverrideAffected);
            }
        }

        [Fact]
        public void OverriddenPrerequisiteMarksTheDependentEvaluation()
        {
            // The LaunchDarkly copy of the prerequisite is off, so the dependent flag passes only
            // through the override.
            var ldPrereq = new FeatureFlagBuilder("prereq").Version(1).On(false).OffVariation(0)
                .Variations(false, true).Build();
            var dependent = new FeatureFlagBuilder("dependent").Version(1).On(true).OffVariation(0)
                .FallthroughVariation(1).Variations(false, true)
                .Prerequisites(new Prerequisite("prereq", 1)).Build();
            var overridePrereq = new FeatureFlagBuilder("prereq").Version(2).On(true).OffVariation(0)
                .FallthroughVariation(1).Variations(false, true).Build();
            var source = new TestOverrideSource(FlagsOnly(overridePrereq));
            using (var client = MakeInitializedClient(FlagsOnly(ldPrereq, dependent), source))
            {
                var detail = client.BoolVariationDetail("dependent", context, false);
                Assert.True(detail.Value);
                Assert.Equal(EvaluationReasonKind.Fallthrough, detail.Reason.Kind);
                Assert.True(detail.Reason.OverrideAffected);
            }
        }

        [Fact]
        public void WrongTypeResultOfOverriddenFlagStaysMarked()
        {
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of("not-a-bool"))));
            using (var client = MakeUninitializedClient(source))
            {
                var detail = client.BoolVariationDetail("overridden-flag", context, false);
                Assert.False(detail.Value);
                Assert.Null(detail.VariationIndex);
                Assert.Equal(EvaluationReasonKind.Error, detail.Reason.Kind);
                Assert.Equal(EvaluationErrorKind.WrongType, detail.Reason.ErrorKind);
                Assert.True(detail.Reason.OverrideAffected);
                Assert.Equal(LdValue.Parse(@"{""kind"":""ERROR"",""errorKind"":""WRONG_TYPE"",""overrideAffected"":true}"),
                    LdValue.Parse(LdJsonSerialization.SerializeObject(detail.Reason)));
            }
        }

        [Fact]
        public void AllFlagsStateContainsOnlyOverridesWhenClientIsNotInitialized()
        {
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
            using (var client = MakeUninitializedClient(source))
            {
                var state = client.AllFlagsState(context);
                Assert.True(state.Valid);
                var values = state.ToValuesJsonMap();
                Assert.Single(values);
                Assert.Equal(LdValue.Of(true), values["overridden-flag"]);
            }
        }

        [Fact]
        public void AllFlagsStateOverridesOnlyWarningIsLoggedOnce()
        {
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
            using (var client = MakeUninitializedClient(source))
            {
                Assert.True(client.AllFlagsState(context).Valid);
                Assert.True(client.AllFlagsState(context).Valid);

                var matching = LogCapture.GetMessages().Where(m =>
                    m.Level == LogLevel.Warn && m.Text.Contains("returning only flags from the override layer")).ToList();
                Assert.Single(matching);
            }
        }

        [Fact]
        public void AllFlagsStateIsInvalidWhenNotInitializedAndOverrideLayerIsEmpty()
        {
            var source = new TestOverrideSource();
            using (var client = MakeUninitializedClient(source))
            {
                var state = client.AllFlagsState(context);
                Assert.False(state.Valid);
                Assert.Empty(state.ToValuesJsonMap());
            }
        }

        [Fact]
        public void AllFlagsStateTurnsOffEventTrackingForOverrideAffectedFlags()
        {
            var debugUntil = UnixMillisecondTime.Now.PlusMillis(100000);
            var plainTracked = new FeatureFlagBuilder("plain-tracked").Version(1).OffWithValue(LdValue.Of(true))
                .TrackEvents(true).DebugEventsUntilDate(debugUntil).Build();
            var dependentTracked = new FeatureFlagBuilder("dependent-tracked").Version(1).On(true).OffVariation(0)
                .FallthroughVariation(1).Variations(false, true)
                .Prerequisites(new Prerequisite("overridden-flag", 0))
                .TrackEvents(true).DebugEventsUntilDate(debugUntil).Build();
            // The overridden flag is on and serves variation 0, so the dependent flag's prerequisite passes.
            var overriddenTracked = new FeatureFlagBuilder("overridden-flag").Version(7).On(true).OffVariation(0)
                .FallthroughVariation(0).Variations(LdValue.Of(true))
                .TrackEvents(true).DebugEventsUntilDate(debugUntil).Build();
            var source = new TestOverrideSource(FlagsOnly(overriddenTracked));
            using (var client = MakeInitializedClient(FlagsOnly(plainTracked, dependentTracked), source))
            {
                var state = client.AllFlagsState(context, FlagsStateOption.WithReasons);
                Assert.True(state.Valid);
                var json = LdValue.Parse(LdJsonSerialization.SerializeObject(state));
                var flagsState = json.Get("$flagsState");

                // A flag with no override keeps its tracking fields.
                var plain = flagsState.Get("plain-tracked");
                Assert.Equal(LdValue.Of(true), plain.Get("trackEvents"));
                Assert.Equal(LdValue.Of(debugUntil.Value), plain.Get("debugEventsUntilDate"));

                // The overridden flag and the flag that depends on it stay in the state with their
                // values and marked reasons, but with no tracking fields.
                foreach (var key in new[] { "overridden-flag", "dependent-tracked" })
                {
                    var entry = flagsState.Get(key);
                    Assert.Equal(LdValue.Of(true), entry.Get("reason").Get("overrideAffected"));
                    Assert.Equal(LdValue.Null, entry.Get("trackEvents"));
                    Assert.Equal(LdValue.Null, entry.Get("trackReason"));
                    Assert.Equal(LdValue.Null, entry.Get("debugEventsUntilDate"));
                }
                Assert.Equal(LdValue.Of(true), json.Get("overridden-flag"));
                Assert.Equal(LdValue.Of(true), json.Get("dependent-tracked"));
                Assert.Equal(LdValue.Of(7), flagsState.Get("overridden-flag").Get("version"));
            }
        }

        [Fact]
        public void FlagTrackerIsNotifiedOfOverrideChanges()
        {
            var source = new TestOverrideSource();
            using (var client = MakeUninitializedClient(source))
            {
                var events = new EventSink<FlagChangeEvent>();
                client.FlagTracker.FlagChanged += events.Add;

                source.SetOverrides(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
                Assert.Equal("overridden-flag", events.ExpectValue(TimeSpan.FromSeconds(5)).Key);

                source.SetOverrides(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(false))));
                Assert.Equal("overridden-flag", events.ExpectValue(TimeSpan.FromSeconds(5)).Key);

                source.SetOverrides(FullDataSet<ItemDescriptor>.Empty());
                Assert.Equal("overridden-flag", events.ExpectValue(TimeSpan.FromSeconds(5)).Key);
            }
        }

        [Fact]
        public void FlagValueChangeListenerSeesOverrideValueChanges()
        {
            var ldFlag = new FeatureFlagBuilder("flag").Version(1).OffWithValue(LdValue.Of("ld-value")).Build();
            var source = new TestOverrideSource();
            using (var client = MakeInitializedClient(FlagsOnly(ldFlag), source))
            {
                var events = new EventSink<FlagValueChangeEvent>();
                client.FlagTracker.FlagChanged += client.FlagTracker.FlagValueChangeHandler("flag", context, events.Add);

                source.SetOverrides(FlagsOnly(SingleValueFlag("flag", LdValue.Of("override-value"))));
                var e = events.ExpectValue(TimeSpan.FromSeconds(5));
                Assert.Equal(LdValue.Of("ld-value"), e.OldValue);
                Assert.Equal(LdValue.Of("override-value"), e.NewValue);
            }
        }

        [Fact]
        public void OverrideSourceIsStartedDuringConstructionAndDisposedWithTheClient()
        {
            var source = new TestOverrideSource();
            var client = MakeUninitializedClient(source);
            Assert.True(source.Started);
            Assert.False(source.Disposed);
            client.Dispose();
            Assert.True(source.Disposed);
        }

        [Fact]
        public void OverrideSourceIsNotStartedWhenOffline()
        {
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
            var config = BasicConfig().Offline(true)
                .DataSystem(Components.DataSystem().Custom().Overrides(source))
                .Build();
            using (var client = new LdClient(config))
            {
                Assert.False(source.Started);
                Assert.False(client.BoolVariation("overridden-flag", context, false));
            }
        }

        [Fact]
        public void OverrideSourceConfigurationErrorPropagatesFromTheConstructor()
        {
            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom().Overrides(new FailingOverrideSourceConfigurer()))
                .Build();
            var e = Assert.Throws<ArgumentException>(() => new LdClient(config));
            Assert.Contains("no file paths", e.Message);
        }

        [Fact]
        public void ClientWithoutOverridesBehavesAsBefore()
        {
            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceThatNeverStarts()))
                .Build();
            using (var client = new LdClient(config))
            {
                Assert.False(client._dataSystem.OverridesConfigured);
                var detail = client.BoolVariationDetail("flag", context, false);
                Assert.Equal(EvaluationReason.ErrorReason(EvaluationErrorKind.ClientNotReady), detail.Reason);
                Assert.False(client.AllFlagsState(context).Valid);
            }
        }

        [Fact]
        public void InvalidContextOnUninitializedClientIsNotReadyWhenTheLayerLacksTheFlag()
        {
            // The layer does not hold the flag, so the evaluation takes the same path as without an
            // override source: the not-ready result comes before the context is examined, and no
            // event is recorded.
            var events = new MockEventProcessor();
            using (var client = MakeUninitializedClient(new TestOverrideSource(), events))
            {
                var detail = client.BoolVariationDetail("flag", Context.New(""), false);
                Assert.Equal(EvaluationReason.ErrorReason(EvaluationErrorKind.ClientNotReady), detail.Reason);
                Assert.Empty(events.Events);
            }
        }

        [Fact]
        public void NullFlagKeyOnUninitializedClientIsNotReady()
        {
            // The layer holds entries of the flag kind, but no entry has a null key. The evaluation
            // takes the same path as without an override source.
            var source = new TestOverrideSource(FlagsOnly(SingleValueFlag("overridden-flag", LdValue.Of(true))));
            using (var client = MakeUninitializedClient(source))
            {
                var detail = client.BoolVariationDetail(null, context, false);
                Assert.Equal(EvaluationReason.ErrorReason(EvaluationErrorKind.ClientNotReady), detail.Reason);
            }
        }

        [Fact]
        public void ExceptionWhileEvaluatingAnOverriddenFlagKeepsTheMarking()
        {
            var source = new TestOverrideSource(FlagsOnly(
                SingleValueFlag(Evaluator.FlagKeyToTriggerErrorForTesting, LdValue.Of(true))));
            var events = new MockEventProcessor();
            using (var client = MakeUninitializedClient(source, events))
            {
                var detail = client.BoolVariationDetail(Evaluator.FlagKeyToTriggerErrorForTesting, context, false);

                // The definition came from the override layer, so the error reason and the recorded
                // event carry the override indicator.
                Assert.False(detail.Value);
                Assert.Equal(EvaluationReason.ErrorReason(EvaluationErrorKind.Exception).WithOverrideAffected(true),
                    detail.Reason);
                var e = Assert.IsType<EventProcessorTypes.EvaluationEvent>(Assert.Single(events.Events));
                Assert.Equal(detail.Reason, e.Reason.Value);
            }
        }

        [Fact]
        public void AllFlagsStateMarksAnExceptionResultOfAnOverriddenFlag()
        {
            var source = new TestOverrideSource(FlagsOnly(
                SingleValueFlag(Evaluator.FlagKeyToTriggerErrorForTesting, LdValue.Of(true))));
            using (var client = MakeUninitializedClient(source))
            {
                var state = client.AllFlagsState(context, FlagsStateOption.WithReasons);

                Assert.True(state.Valid);
                var entry = LdValue.Parse(LdJsonSerialization.SerializeObject(state))
                    .Get("$flagsState").Get(Evaluator.FlagKeyToTriggerErrorForTesting);
                Assert.Equal(LdValue.Of("ERROR"), entry.Get("reason").Get("kind"));
                Assert.Equal(LdValue.Of(true), entry.Get("reason").Get("overrideAffected"));
                Assert.Equal(LdValue.Null, entry.Get("trackEvents"));
            }
        }

        [Fact]
        public void OverrideSourceStartFailureDisposesTheClientComponentsAndPropagates()
        {
            var source = new TestOverrideSource { StartError = new InvalidOperationException("source failed") };
            var events = new MockEventProcessor();
            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceThatNeverStarts())
                    .Overrides(source))
                .Events(events.AsSingletonFactory<IEventProcessor>())
                .Build();

            var e = Assert.Throws<InvalidOperationException>(() => new LdClient(config));

            Assert.Equal("source failed", e.Message);
            Assert.True(source.Disposed);
            Assert.True(events.Disposed);
        }

        [Fact]
        public void OverrideSourceBuildFailureDisposesThePersistentStore()
        {
            var store = new DisposalTrackingDataStore();
            var config = BasicConfig()
                .DataSystem(Components.DataSystem().Custom()
                    .Synchronizers(MockComponents.MockDataSourceThatNeverStarts())
                    .PersistentStore(store.AsSingletonFactory<IDataStore>(), DataSystemConfiguration.DataStoreMode.ReadWrite)
                    .Overrides(new FailingOverrideSourceConfigurer()))
                .Build();

            Assert.Throws<ArgumentException>(() => new LdClient(config));

            Assert.True(store.Disposed);
        }

        private class DisposalTrackingDataStore : IDataStore
        {
            public volatile bool Disposed;

            public bool StatusMonitoringEnabled => false;

            public void Dispose() => Disposed = true;

            public ItemDescriptor? Get(DataKind kind, string key) => null;

            public KeyedItems<ItemDescriptor> GetAll(DataKind kind) =>
                new KeyedItems<ItemDescriptor>(Enumerable.Empty<KeyValuePair<string, ItemDescriptor>>());

            public void Init(FullDataSet<ItemDescriptor> allData) { }

            public bool Initialized() => false;

            public bool Upsert(DataKind kind, string key, ItemDescriptor item) => true;
        }

        private class FailingOverrideSourceConfigurer : IComponentConfigurer<IOverrideSource>
        {
            public IOverrideSource Build(LdClientContext context) =>
                throw new ArgumentException("no file paths were specified");
        }
    }
}
