using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Subsystems.EventProcessorTypes;

namespace LaunchDarkly.Sdk.Server
{
    // Analytics event behavior of override-affected evaluations: the evaluation records the client
    // hands to the event processor carry the marking, and the event output contains no individual
    // feature or debug event for a marked evaluation while its summary counter carries the marker.
    public class LdClientOverrideEventsTest : BaseTest
    {
        private static readonly Context context = Context.New("userkey");

        // The prerequisite tree used by the marking tests. Every flag requests individual events.
        //
        //   top-flag (LaunchDarkly) --> mid-flag (LaunchDarkly) --> leaf-flag (overridden)
        //                           --> plain-flag (LaunchDarkly)
        //
        // The LaunchDarkly copy of leaf-flag is off, so the chain passes only through the override.
        private const string TopKey = "top-flag";
        private const string MidKey = "mid-flag";
        private const string LeafKey = "leaf-flag";
        private const string PlainKey = "plain-flag";

        public LdClientOverrideEventsTest(ITestOutputHelper testOutput) : base(testOutput) { }

        private static FeatureFlagBuilder TrackedBoolFlag(string key) =>
            new FeatureFlagBuilder(key).Version(1).Variations(false, true).OffVariation(0).FallthroughVariation(1)
                .TrackEvents(true);

        private static FullDataSet<ItemDescriptor> PrerequisiteTreeLaunchDarklyData() =>
            LdClientOverridesTest.FlagsOnly(
                TrackedBoolFlag(TopKey).On(true).Prerequisites(new Prerequisite(MidKey, 1), new Prerequisite(PlainKey, 1)).Build(),
                TrackedBoolFlag(MidKey).On(true).Prerequisites(new Prerequisite(LeafKey, 1)).Build(),
                TrackedBoolFlag(PlainKey).On(true).Build(),
                TrackedBoolFlag(LeafKey).On(false).Build());

        private static FullDataSet<ItemDescriptor> PrerequisiteTreeOverrides() =>
            LdClientOverridesTest.FlagsOnly(TrackedBoolFlag(LeafKey).Version(2).On(true).Build());

        // A null data set means the client can never obtain LaunchDarkly data.
        private LdClient MakeClient(FullDataSet<ItemDescriptor>? launchDarklyData, TestOverrideSource source,
            IComponentConfigurer<IEventProcessor> events)
        {
            var dataSystem = Components.DataSystem().Custom().Overrides(source);
            var config = BasicConfig().Events(events);
            if (launchDarklyData.HasValue)
            {
                dataSystem.Synchronizers(MockComponents.MockDataSourceWithData(launchDarklyData.Value));
                config.StartWaitTime(TimeSpan.FromSeconds(5));
            }
            else
            {
                dataSystem.Synchronizers(MockComponents.MockDataSourceThatNeverStarts());
            }
            return new LdClient(config.DataSystem(dataSystem).Build());
        }

        private static Dictionary<string, EvaluationEvent> EvaluationRecordsByKey(MockEventProcessor events) =>
            events.Events.OfType<EvaluationEvent>().ToDictionary(e => e.FlagKey, e => e);

        [Fact]
        public void OverrideEvaluationEventCarriesTheMarking()
        {
            var events = new MockEventProcessor();
            var source = new TestOverrideSource(LdClientOverridesTest.FlagsOnly(
                LdClientOverridesTest.SingleValueFlag("overridden-flag", LdValue.Of(true))));
            using (var client = MakeClient(null, source, events.AsSingletonFactory<IEventProcessor>()))
            {
                Assert.True(client.BoolVariation("overridden-flag", context, false));

                var records = EvaluationRecordsByKey(events);
                Assert.Single(records);
                Assert.True(records["overridden-flag"].OverrideAffected);
                // The marking is carried even though the caller did not ask for reasons.
                Assert.Null(records["overridden-flag"].Reason);
            }
        }

        [Fact]
        public void PlainEvaluationEventIsNotMarked()
        {
            var events = new MockEventProcessor();
            var ldFlag = new FeatureFlagBuilder("flag").Version(1).OffWithValue(LdValue.Of(true)).Build();
            using (var client = MakeClient(LdClientOverridesTest.FlagsOnly(ldFlag), new TestOverrideSource(),
                events.AsSingletonFactory<IEventProcessor>()))
            {
                Assert.True(client.BoolVariation("flag", context, false));
                Assert.False(EvaluationRecordsByKey(events)["flag"].OverrideAffected);
            }
        }

        [Fact]
        public void OverriddenPrerequisiteMarksTheDependentEvaluationRecords()
        {
            var events = new MockEventProcessor();
            var source = new TestOverrideSource(PrerequisiteTreeOverrides());
            using (var client = MakeClient(PrerequisiteTreeLaunchDarklyData(), source, events.AsSingletonFactory<IEventProcessor>()))
            {
                var detail = client.BoolVariationDetail(TopKey, context, false);
                Assert.True(detail.Value);
                Assert.Equal(EvaluationReasonKind.Fallthrough, detail.Reason.Kind);
                Assert.True(detail.Reason.OverrideAffected);

                var records = EvaluationRecordsByKey(events);
                Assert.Equal(4, records.Count);

                // The top-level flag came from LaunchDarkly. Its record is marked because a definition
                // read during its evaluation came from the override store.
                Assert.True(records[TopKey].OverrideAffected);
                Assert.Null(records[TopKey].PrerequisiteOf);

                // The intermediate prerequisite also came from LaunchDarkly. Its own subtree read the
                // override, so its record is marked.
                Assert.True(records[MidKey].OverrideAffected);
                Assert.Equal(TopKey, records[MidKey].PrerequisiteOf);

                // The overridden leaf is marked directly, and it is recorded with the override version.
                Assert.True(records[LeafKey].OverrideAffected);
                Assert.Equal(MidKey, records[LeafKey].PrerequisiteOf);
                Assert.Equal(2, records[LeafKey].FlagVersion);

                // The sibling prerequisite read nothing from the override store, so it is not marked.
                Assert.False(records[PlainKey].OverrideAffected);
                Assert.Equal(TopKey, records[PlainKey].PrerequisiteOf);

                // Every flag requested individual events. The marking alone decides which records the
                // event processor keeps out of the individual event stream.
                Assert.All(records.Values, r => Assert.True(r.TrackEvents));
            }
        }

        [Fact]
        public void WrongTypeEventOfOverriddenFlagIsMarked()
        {
            var events = new MockEventProcessor();
            var source = new TestOverrideSource(LdClientOverridesTest.FlagsOnly(
                LdClientOverridesTest.SingleValueFlag("overridden-flag", LdValue.Of("not-a-bool"))));
            using (var client = MakeClient(null, source, events.AsSingletonFactory<IEventProcessor>()))
            {
                var detail = client.BoolVariationDetail("overridden-flag", context, false);
                Assert.Equal(EvaluationErrorKind.WrongType, detail.Reason.ErrorKind);

                var record = EvaluationRecordsByKey(events)["overridden-flag"];
                Assert.True(record.OverrideAffected);
                Assert.Equal(LdValue.Of(false), record.Value);
                Assert.Null(record.Variation);
                Assert.True(record.Reason.HasValue);
                Assert.True(record.Reason.Value.OverrideAffected);
                Assert.Equal(EvaluationErrorKind.WrongType, record.Reason.Value.ErrorKind);
            }
        }

        [Fact]
        public void OverrideAffectedEvaluationsAppearOnlyInSummaryOutput()
        {
            var sender = new MockEventSender { FilterKind = EventDataKind.AnalyticsEvents };
            var events = Components.SendEvents().EventSender(sender).FlushInterval(TimeSpan.FromHours(1));
            var source = new TestOverrideSource(PrerequisiteTreeOverrides());
            using (var client = MakeClient(PrerequisiteTreeLaunchDarklyData(), source, events))
            {
                Assert.True(client.BoolVariation(TopKey, context, false));
                Assert.True(client.FlushAndWait(TimeSpan.FromSeconds(5)));
            }

            var payload = LdValue.Parse(sender.RequirePayload().Data);
            var featureEventKeys = new List<string>();
            var debugEventKeys = new List<string>();
            LdValue summary = LdValue.Null;
            foreach (var e in payload.List)
            {
                switch (e.Get("kind").AsString)
                {
                    case "feature":
                        featureEventKeys.Add(e.Get("key").AsString);
                        break;
                    case "debug":
                        debugEventKeys.Add(e.Get("key").AsString);
                        break;
                    case "summary":
                        summary = e;
                        break;
                }
            }

            // Only the unaffected sibling produces an individual event, even though every flag in the
            // tree has event tracking on.
            Assert.Equal(new[] { PlainKey }, featureEventKeys);
            Assert.Empty(debugEventKeys);

            Assert.False(summary.IsNull);
            var features = summary.Get("features");
            foreach (var key in new[] { TopKey, MidKey, LeafKey, PlainKey })
            {
                var counters = features.Get(key).Get("counters");
                Assert.True(counters.Count == 1, key + ": expected one counter");
                var marker = counters.Get(0).Get("overrideAffected");
                if (key == PlainKey)
                {
                    Assert.True(marker.IsNull, key + ": the marker is omitted for an unaffected counter");
                }
                else
                {
                    Assert.Equal(LdValue.Of(true), marker);
                }
            }
        }

        [Fact]
        public void DefaultEventProcessorWrapperPassesTheMarkingThrough()
        {
            var sender = new MockEventSender { FilterKind = EventDataKind.AnalyticsEvents };
            var processor = Components.SendEvents().EventSender(sender).FlushInterval(TimeSpan.FromHours(1))
                .Build(ContextFrom(BasicConfig().Build()));
            using (processor)
            {
                processor.RecordEvaluationEvent(new EvaluationEvent
                {
                    Timestamp = UnixMillisecondTime.Now,
                    Context = context,
                    FlagKey = "flag",
                    FlagVersion = 3,
                    Variation = 0,
                    Value = LdValue.Of(true),
                    Default = LdValue.Of(false),
                    TrackEvents = true,
                    OverrideAffected = true
                });
                Assert.True(processor.FlushAndWait(TimeSpan.FromSeconds(5)));
            }
            var payload = LdValue.Parse(sender.RequirePayload().Data);
            Assert.DoesNotContain(payload.List, e => e.Get("kind").AsString == "feature");
            var counters = payload.List.First(e => e.Get("kind").AsString == "summary")
                .Get("features").Get("flag").Get("counters");
            Assert.Equal(LdValue.Of(true), counters.Get(0).Get("overrideAffected"));
        }
    }
}
