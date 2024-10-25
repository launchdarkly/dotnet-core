using System;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Subsystems;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.Sdk.Client.Subsystems.EventProcessorTypes;

namespace LaunchDarkly.Sdk.Client
{
    public class LdClientEventTests : BaseTest
    {
        private static readonly Context user = Context.New("userkey");
        private readonly TestData _testData = TestData.DataSource();
        private MockEventProcessor eventProcessor = new MockEventProcessor();
        private IComponentConfigurer<IEventProcessor> _factory;
        private ITestOutputHelper _testOutput;

        public LdClientEventTests(ITestOutputHelper testOutput) : base(testOutput)
        {
            _factory = eventProcessor.AsSingletonFactory<IEventProcessor>();
            _testOutput = testOutput;
        }

        private LdClient MakeClient(Context c) =>
            LdClient.Init(BasicConfig().DataSource(_testData).Events(_factory).Build(),
                c, TimeSpan.FromSeconds(1));

        [Fact]
        public void IdentifySendsIdentifyEvent()
        {
            using (LdClient client = MakeClient(user))
            {
                Context user1 = Context.New("userkey1");
                client.Identify(user1, TimeSpan.FromSeconds(1));
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user), // there's always an initial identify event
                    e => CheckIdentifyEvent(e, user1));
            }
        }

        [Fact]
        public void TrackSendsCustomEvent()
        {
            using (LdClient client = MakeClient(user))
            {
                client.Track("eventkey");
                Assert.Collection(eventProcessor.Events, 
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        CustomEvent ce = Assert.IsType<CustomEvent>(e);
                        Assert.Equal("eventkey", ce.EventKey);
                        Assert.Equal(user.FullyQualifiedKey, ce.Context.FullyQualifiedKey);
                        Assert.Equal(LdValue.Null, ce.Data);
                        Assert.Null(ce.MetricValue);
                        Assert.NotEqual(0, ce.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void TrackWithDataSendsCustomEvent()
        {
            using (LdClient client = MakeClient(user))
            {
                LdValue data = LdValue.Of("hi");
                client.Track("eventkey", data);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        CustomEvent ce = Assert.IsType<CustomEvent>(e);
                        Assert.Equal("eventkey", ce.EventKey);
                        Assert.Equal(user.FullyQualifiedKey, ce.Context.FullyQualifiedKey);
                        Assert.Equal(data, ce.Data);
                        Assert.Null(ce.MetricValue);
                        Assert.NotEqual(0, ce.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void TrackWithMetricValueSendsCustomEvent()
        {
            using (LdClient client = MakeClient(user))
            {
                LdValue data = LdValue.Of("hi");
                double metricValue = 1.5;
                client.Track("eventkey", data, metricValue);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        CustomEvent ce = Assert.IsType<CustomEvent>(e);
                        Assert.Equal("eventkey", ce.EventKey);
                        Assert.Equal(user.FullyQualifiedKey, ce.Context.FullyQualifiedKey);
                        Assert.Equal(data, ce.Data);
                        Assert.Equal(metricValue, ce.MetricValue);
                        Assert.NotEqual(0, ce.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationSendsFeatureEventForValidFlag()
        {
            var flag = new FeatureFlagBuilder().Value(LdValue.Of("a")).Variation(1).Version(1000)
                .TrackEvents(true).DebugEventsUntilDate(UnixMillisecondTime.OfMillis(2000)).Build();
            _testData.Update(_testData.Flag("flag").PreconfiguredFlag(flag));
            using (LdClient client = MakeClient(user))
            {
                string result = client.StringVariation("flag", "b");
                Assert.Equal("a", result);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("a", fe.Value.AsString);
                        Assert.Equal(1, fe.Variation);
                        Assert.Equal(1000, fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.True(fe.TrackEvents);
                        Assert.Equal(UnixMillisecondTime.OfMillis(2000), fe.DebugEventsUntilDate);
                        Assert.Null(fe.Reason);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void FeatureEventUsesFlagVersionIfProvided()
        {
            var flag = new FeatureFlagBuilder().Value(LdValue.Of("a")).Variation(1).Version(1000)
                .FlagVersion(1500).Build();
            _testData.Update(_testData.Flag("flag").PreconfiguredFlag(flag));
            using (LdClient client = MakeClient(user))
            {
                string result = client.StringVariation("flag", "b");
                Assert.Equal("a", result);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("a", fe.Value.AsString);
                        Assert.Equal(1, fe.Variation);
                        Assert.Equal(1500, fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationSendsFeatureEventForDefaultValue()
        {
            var flag = new FeatureFlagBuilder().Version(1000).Build();
            _testData.Update(_testData.Flag("flag").PreconfiguredFlag(flag));
            using (LdClient client = MakeClient(user))
            {
                string result = client.StringVariation("flag", "b");
                Assert.Equal("b", result);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("b", fe.Value.AsString);
                        Assert.Null(fe.Variation);
                        Assert.Equal(1000, fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.Null(fe.Reason);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationSendsFeatureEventForUnknownFlag()
        {
            using (LdClient client = MakeClient(user))
            {
                string result = client.StringVariation("flag", "b");
                Assert.Equal("b", result);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("b", fe.Value.AsString);
                        Assert.Null(fe.Variation);
                        Assert.Null(fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.Null(fe.Reason);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationSendsFeatureEventForUnknownFlagWhenClientIsNotInitialized()
        {
            var config = BasicConfig()
                .DataSource(new MockDataSourceThatNeverInitializes().AsSingletonFactory<IDataSource>())
                .Events(_factory);

            using (LdClient client = TestUtil.CreateClient(config.Build(), user))
            {
                string result = client.StringVariation("flag", "b");
                Assert.Equal("b", result);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e =>
                    {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("b", fe.Value.AsString);
                        Assert.Null(fe.Variation);
                        Assert.Null(fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.Null(fe.Reason);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationSendsFeatureEventWithTrackingAndReasonIfTrackReasonIsTrue()
        {
            var flag = new FeatureFlagBuilder().Value(LdValue.Of("a")).Variation(1).Version(1000)
                .TrackReason(true).Reason(EvaluationReason.OffReason).Build();
            _testData.Update(_testData.Flag("flag").PreconfiguredFlag(flag));
            using (LdClient client = MakeClient(user))
            {
                string result = client.StringVariation("flag", "b");
                Assert.Equal("a", result);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("a", fe.Value.AsString);
                        Assert.Equal(1, fe.Variation);
                        Assert.Equal(1000, fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.True(fe.TrackEvents);
                        Assert.Null(fe.DebugEventsUntilDate);
                        Assert.Equal(EvaluationReason.OffReason, fe.Reason);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationDetailSendsFeatureEventWithReasonForValidFlag()
        {
            var flag = new FeatureFlagBuilder().Value(LdValue.Of("a")).Variation(1).Version(1000)
                .TrackEvents(true).DebugEventsUntilDate(UnixMillisecondTime.OfMillis(2000))
                .Reason(EvaluationReason.OffReason).Build();
            _testData.Update(_testData.Flag("flag").PreconfiguredFlag(flag));
            using (LdClient client = MakeClient(user))
            {
                EvaluationDetail<string> result = client.StringVariationDetail("flag", "b");
                Assert.Equal("a", result.Value);
                Assert.Equal(EvaluationReason.OffReason, result.Reason);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("a", fe.Value.AsString);
                        Assert.Equal(1, fe.Variation);
                        Assert.Equal(1000, fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.True(fe.TrackEvents);
                        Assert.Equal(UnixMillisecondTime.OfMillis(2000), fe.DebugEventsUntilDate);
                        Assert.Equal(EvaluationReason.OffReason, fe.Reason);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationDetailSendsFeatureEventWithReasonForUnknownFlag()
        {
            using (LdClient client = MakeClient(user))
            {
                EvaluationDetail<string> result = client.StringVariationDetail("flag", "b");
                var expectedReason = EvaluationReason.ErrorReason(EvaluationErrorKind.FlagNotFound);
                Assert.Equal("b", result.Value);
                Assert.Equal(expectedReason, result.Reason);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("b", fe.Value.AsString);
                        Assert.Null(fe.Variation);
                        Assert.Null(fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.False(fe.TrackEvents);
                        Assert.Null(fe.DebugEventsUntilDate);
                        Assert.Equal(expectedReason, fe.Reason);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationSendsFeatureEventWithReasonForUnknownFlagWhenClientIsNotInitialized()
        {
            var config = BasicConfig()
                .DataSource(new MockDataSourceThatNeverInitializes().AsSingletonFactory<IDataSource>())
                .Events(_factory);

            using (LdClient client = TestUtil.CreateClient(config.Build(), user))
            {
                EvaluationDetail<string> result = client.StringVariationDetail("flag", "b");
                var expectedReason = EvaluationReason.ErrorReason(EvaluationErrorKind.ClientNotReady);
                Assert.Equal("b", result.Value);
                Assert.Equal(expectedReason, result.Reason);
                Assert.Collection(eventProcessor.Events,
                    e => CheckIdentifyEvent(e, user),
                    e => {
                        EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
                        Assert.Equal("flag", fe.FlagKey);
                        Assert.Equal("b", fe.Value.AsString);
                        Assert.Null(fe.Variation);
                        Assert.Null(fe.FlagVersion);
                        Assert.Equal("b", fe.Default.AsString);
                        Assert.False(fe.TrackEvents);
                        Assert.Null(fe.DebugEventsUntilDate);
                        Assert.Equal(expectedReason, fe.Reason);
                        Assert.NotEqual(0, fe.Timestamp.Value);
                    });
            }
        }

        [Fact]
        public void VariationSendsFeatureEventForPrerequisites()
        {
            var flagA = new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(1).Version(1000)
                .TrackEvents(false).TrackReason(false).Build();
            var flagAB = new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(1).Version(1000)
                .TrackEvents(false).TrackReason(false).Prerequisites("flagA").Build();
            var flagAC = new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(1).Version(1000)
                .TrackEvents(false).TrackReason(false).Prerequisites("flagA").Build();
            var flagABD = new FeatureFlagBuilder().Value(LdValue.Of(true)).Variation(1).Version(1000)
                .TrackEvents(false).TrackReason(false).Prerequisites("flagAB").Build();

            _testData.Update(_testData.Flag("flagA").PreconfiguredFlag(flagA));
            _testData.Update(_testData.Flag("flagAB").PreconfiguredFlag(flagAB));
            _testData.Update(_testData.Flag("flagAC").PreconfiguredFlag(flagAC));
            _testData.Update(_testData.Flag("flagABD").PreconfiguredFlag(flagABD));

            using (LdClient client = MakeClient(user))
            {
                client.BoolVariation("flagA");
                client.BoolVariation("flagAB");
                client.BoolVariation("flagAC");
                client.BoolVariation("flagABD");

                _testOutput.WriteLine("Events:");
                foreach (var e in eventProcessor.Events)
                {
                    _testOutput.WriteLine(e.ToString());
                }

                Assert.Collection(eventProcessor.Events,
                e => CheckIdentifyEvent(e, user),
                    e => CheckEvaluationEvent(e, "flagA"),
                    e => CheckEvaluationEvent(e, "flagA"),
                    e => CheckEvaluationEvent(e, "flagAB"),
                    e => CheckEvaluationEvent(e, "flagA"),
                    e => CheckEvaluationEvent(e, "flagAC"),
                    e => CheckEvaluationEvent(e, "flagA"),
                    e => CheckEvaluationEvent(e, "flagAB"),
                    e => CheckEvaluationEvent(e, "flagABD")
                );
            }
        }

        private void CheckIdentifyEvent(object e, Context c)
        {
            IdentifyEvent ie = Assert.IsType<IdentifyEvent>(e);
            Assert.Equal(c.FullyQualifiedKey, ie.Context.FullyQualifiedKey);
            Assert.NotEqual(0, ie.Timestamp.Value);
        }

        private void CheckEvaluationEvent(object e, string flagKey)
        {
            EvaluationEvent fe = Assert.IsType<EvaluationEvent>(e);
            Assert.Equal(flagKey, fe.FlagKey);
        }
    }
}
