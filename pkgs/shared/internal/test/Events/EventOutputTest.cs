using System;
using Xunit;
using static LaunchDarkly.Sdk.Internal.Events.EventProcessorInternal;
using static LaunchDarkly.Sdk.Internal.Events.EventTypes;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventOutputTest
    {
        private static readonly UnixMillisecondTime _fixedTimestamp = UnixMillisecondTime.OfMillis(100000);
        private static readonly Context SimpleContext = Context.Builder("userkey").Name("me").Build();
        private const string SimpleContextJson = @"{""kind"": ""user"", ""key"":""userkey"", ""name"": ""me""}";

        [Fact]
        public void EvaluationEventIsSerialized()
        {
            Func<EvaluationEvent> MakeBasicEvent = () => new EvaluationEvent
            {
                Timestamp = _fixedTimestamp,
                FlagKey = "flag",
                FlagVersion = 11,
                Context = SimpleContext,
                Value = LdValue.Of("flagvalue"),
                Default = LdValue.Of("defaultvalue")
            };
            var fe = MakeBasicEvent();
            TestEventSerialization(fe, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""context"":" + SimpleContextJson + @",
                ""value"":""flagvalue"",
                ""default"":""defaultvalue""
                }"));

            var feWithVariation = MakeBasicEvent();
            feWithVariation.Variation = 1;
            TestEventSerialization(feWithVariation, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""context"":" + SimpleContextJson + @",
                ""value"":""flagvalue"",
                ""variation"":1,
                ""default"":""defaultvalue""
                }"));

            var feWithReason = MakeBasicEvent();
            feWithReason.Variation = 1;
            feWithReason.Reason = EvaluationReason.RuleMatchReason(1, "id");
            TestEventSerialization(feWithReason, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""context"":" + SimpleContextJson + @",
                ""value"":""flagvalue"",
                ""variation"":1,
                ""default"":""defaultvalue"",
                ""reason"":{""kind"":""RULE_MATCH"",""ruleIndex"":1,""ruleId"":""id""}
                }"));

            var feUnknownFlag = new EvaluationEvent
            {
                Timestamp = fe.Timestamp,
                FlagKey = "flag",
                Context = SimpleContext,
                Value = LdValue.Of("defaultvalue"),
                Default = LdValue.Of("defaultvalue")
            };
            TestEventSerialization(feUnknownFlag, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""context"":" + SimpleContextJson + @",
                ""value"":""defaultvalue"",
                ""default"":""defaultvalue""
                }"));

            var debugEvent = new DebugEvent {FromEvent = feWithVariation};
            TestEventSerialization(debugEvent, LdValue.Parse(@"{
                ""kind"":""debug"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""context"":" + SimpleContextJson + @",
                ""value"":""flagvalue"",
                ""variation"":1,
                ""default"":""defaultvalue""
                }"));
        }

        [Fact]
        public void ItSerializesTheSamplingRatioForFeatureEventsWhenNotOne()
        {
            TestEventSerialization(new EvaluationEvent
            {
                Timestamp = _fixedTimestamp,
                FlagKey = "flag",
                FlagVersion = 11,
                Context = SimpleContext,
                Value = LdValue.Of("flagvalue"),
                Default = LdValue.Of("defaultvalue"),
                SamplingRatio = 2
            }, LdValue.Parse(@"{
                ""kind"":""feature"",
                ""creationDate"":100000,
                ""key"":""flag"",
                ""version"":11,
                ""context"":" + SimpleContextJson + @",
                ""value"":""flagvalue"",
                ""default"":""defaultvalue"",
                ""samplingRatio"": 2
                }"));
        }

        [Fact]
        public void IdentifyEventIsSerialized()
        {
            var user = User.Builder("userkey").Name("me").Build();
            var ie = new IdentifyEvent {Timestamp = _fixedTimestamp, Context = SimpleContext};
            TestEventSerialization(ie, LdValue.Parse(@"{
                ""kind"":""identify"",
                ""creationDate"":100000,
                ""context"":" + SimpleContextJson + @"
                }"));
        }

        [Fact]
        public void CustomEventIsSerialized()
        {
            Func<CustomEvent> MakeBasicEvent = () => new CustomEvent
            {
                Timestamp = _fixedTimestamp,
                EventKey = "customkey",
                Context = SimpleContext
            };
            var ceWithoutData = MakeBasicEvent();
            TestEventSerialization(ceWithoutData, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""context"":" + SimpleContextJson + @"
                }"));

            var ceWithData = MakeBasicEvent();
            ceWithData.Data = LdValue.Of("thing");
            TestEventSerialization(ceWithData, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""context"":" + SimpleContextJson + @",
                ""data"":""thing""
                }"));

            var ceWithMetric = MakeBasicEvent();
            ceWithMetric.MetricValue = 2.5;
            TestEventSerialization(ceWithMetric, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""context"":" + SimpleContextJson + @",
                ""metricValue"":2.5
                }"));

            var ceWithDataAndMetric = MakeBasicEvent();
            ceWithDataAndMetric.Data = ceWithData.Data;
            ceWithDataAndMetric.MetricValue = ceWithMetric.MetricValue;
            TestEventSerialization(ceWithDataAndMetric, LdValue.Parse(@"{
                ""kind"":""custom"",
                ""creationDate"":100000,
                ""key"":""customkey"",
                ""context"":" + SimpleContextJson + @",
                ""data"":""thing"",
                ""metricValue"":2.5
                }"));
        }

        [Fact]
        public void SummaryEventIsSerialized()
        {
            var context1 = Context.New(ContextKind.Of("kind1"), "key1");
            var context2 = Context.New(ContextKind.Of("kind2"), "key2");

            var summary = new EventSummary();
            summary.NoteTimestamp(UnixMillisecondTime.OfMillis(1001));

            summary.IncrementCounter("first", 1, 11, LdValue.Of("value1a"), LdValue.Of("default1"), context1);

            summary.IncrementCounter("second", 1, 21, LdValue.Of("value2a"), LdValue.Of("default2"), context1);

            summary.IncrementCounter("first", 1, 11, LdValue.Of("value1a"), LdValue.Of("default1"), context1);
            summary.IncrementCounter("first", 1, 12, LdValue.Of("value1a"), LdValue.Of("default1"), context1);

            summary.IncrementCounter("second", 2, 21, LdValue.Of("value2b"), LdValue.Of("default2"), context2);
            summary.IncrementCounter("second", null, 21, LdValue.Of("default2"), LdValue.Of("default2"),
                context2); // flag exists (has version), but eval failed (no variation)

            summary.IncrementCounter("third", null, null, LdValue.Of("default3"), LdValue.Of("default3"),
                context2); // flag doesn't exist (no version)

            summary.NoteTimestamp(UnixMillisecondTime.OfMillis(1000));
            summary.NoteTimestamp(UnixMillisecondTime.OfMillis(1002));

            var f = new EventOutputFormatter(new EventsConfiguration());
            var outputEvent = TestUtil.TryParseJson(f.SerializeOutputEvents(new object[0], new[] { summary }, out var count))
                .Get(0);
            Assert.Equal(1, count);

            Assert.Equal("summary", outputEvent.Get("kind").AsString);
            Assert.Equal(1000, outputEvent.Get("startDate").AsInt);
            Assert.Equal(1002, outputEvent.Get("endDate").AsInt);

            // An aggregated summary (undefined context) does not include a context.
            Assert.Equal(LdValue.Null, outputEvent.Get("context"));

            var featuresJson = outputEvent.Get("features");
            Assert.Equal(3, featuresJson.Count);

            var firstJson = featuresJson.Get("first");
            Assert.Equal("default1", firstJson.Get("default").AsString);
            Assert.Equal(LdValue.ArrayOf(LdValue.Of("kind1")),
                firstJson.Get("contextKinds")); // we evaluated this flag with only context1
            TestUtil.AssertContainsInAnyOrder(firstJson.Get("counters").List,
                LdValue.Parse(@"{""value"":""value1a"",""variation"":1,""version"":11,""count"":2}"),
                LdValue.Parse(@"{""value"":""value1a"",""variation"":1,""version"":12,""count"":1}"));

            var secondJson = featuresJson.Get("second");
            Assert.Equal("default2", secondJson.Get("default").AsString);
            TestUtil.AssertContainsInAnyOrder(secondJson.Get("contextKinds").List,
                LdValue.Of("kind1"), LdValue.Of("kind2")); // we evaluated this flag with both context1 and context2
            TestUtil.AssertContainsInAnyOrder(secondJson.Get("counters").List,
                LdValue.Parse(@"{""value"":""value2a"",""variation"":1,""version"":21,""count"":1}"),
                LdValue.Parse(@"{""value"":""value2b"",""variation"":2,""version"":21,""count"":1}"),
                LdValue.Parse(@"{""value"":""default2"",""version"":21,""count"":1}"));

            var thirdJson = featuresJson.Get("third");
            Assert.Equal("default3", thirdJson.Get("default").AsString);
            Assert.Equal(LdValue.ArrayOf(LdValue.Of("kind2")),
                thirdJson.Get("contextKinds")); // we evaluated this flag with only context2
            TestUtil.AssertContainsInAnyOrder(thirdJson.Get("counters").AsList(LdValue.Convert.Json),
                LdValue.Parse(@"{""unknown"":true,""value"":""default3"",""count"":1}"));
        }

        [Fact]
        public void PerContextSummaryEventIncludesTheContext()
        {
            var summary = new EventSummary(SimpleContext);
            summary.NoteTimestamp(UnixMillisecondTime.OfMillis(1000));
            summary.IncrementCounter("flag", 1, 11, LdValue.Of("value"), LdValue.Of("default"), SimpleContext);

            var f = new EventOutputFormatter(new EventsConfiguration());
            var outputEvent = TestUtil.TryParseJson(f.SerializeOutputEvents(new object[0], new[] { summary }, out var count))
                .Get(0);

            Assert.Equal(1, count);
            Assert.Equal("summary", outputEvent.Get("kind").AsString);
            Assert.Equal(LdValue.Parse(SimpleContextJson), outputEvent.Get("context"));
            Assert.NotEqual(LdValue.Null, outputEvent.Get("features").Get("flag"));
        }

        [Fact]
        public void ItSerializesMigrationOpEvents()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                FlagVersion = 12,
                Variation = 2,
                Value = LdValue.Of("live"),
                Default = LdValue.Of("off"),
                Reason = EvaluationReason.FallthroughReason,
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    Old = true,
                    New = true
                },
                Latency = new MigrationOpEvent.LatencyMeasurement
                {
                    Old = 200,
                    New = 300
                },
                Error = new MigrationOpEvent.ErrorMeasurement
                {
                    Old = true,
                    New = true
                },
                Consistent = new MigrationOpEvent.ConsistentMeasurement()
                {
                    IsConsistent = true,
                    SamplingRatio = 3
                }
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""version"":12,
                    ""value"":""live"",
                    ""variation"":2,
                    ""default"":""off"",
                    ""reason"":{""kind"":""FALLTHROUGH""}
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""old"": true,
                            ""new"": true
                        }
                    },
                    {
                        ""key"": ""error"",
                        ""values"": {
                            ""old"": true,
                            ""new"": true
                        }
                    },
                    {
                        ""key"": ""latency_ms"",
                        ""values"": {
                            ""old"": 200,
                            ""new"": 300
                        }
                    },
                    {
                        ""key"": ""consistent"",
                        ""value"": true,
                        ""samplingRatio"": 3
                    }
                ]
            }"));
        }

        [Fact]
        public void ItCanOmitOptionalMeasurements()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                FlagVersion = 12,
                Variation = 2,
                Value = LdValue.Of("live"),
                Default = LdValue.Of("off"),
                Reason = EvaluationReason.FallthroughReason,
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    Old = true,
                    New = true
                },
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""version"":12,
                    ""value"":""live"",
                    ""variation"":2,
                    ""default"":""off"",
                    ""reason"":{""kind"":""FALLTHROUGH""}
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""old"": true,
                            ""new"": true
                        }
                    }
                ]
            }"));
        }

        [Fact]
        public void ItCanOmitOptionalEvaluationDetailFields()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                Value = LdValue.Of("off"),
                Default = LdValue.Of("off"),
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    Old = true,
                    New = true
                },
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""value"":""off"",
                    ""default"":""off""
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""old"": true,
                            ""new"": true
                        }
                    }
                ]
            }"));
        }

        [Fact]
        public void ItCanHandleOnlyOldInvoked()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                Value = LdValue.Of("off"),
                Default = LdValue.Of("off"),
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    Old = true,
                },
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""value"":""off"",
                    ""default"":""off""
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""old"": true
                        }
                    }
                ]
            }"));
        }

        [Fact]
        public void ItCanHandleOnlyNewInvoked()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                Value = LdValue.Of("off"),
                Default = LdValue.Of("off"),
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    New = true,
                },
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""value"":""off"",
                    ""default"":""off""
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""new"": true
                        }
                    }
                ]
            }"));
        }

        [Fact]
        public void ItCanHandleOnlyOldLatency()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                Value = LdValue.Of("off"),
                Default = LdValue.Of("off"),
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    New = true,
                    Old = true
                },
                Latency = new MigrationOpEvent.LatencyMeasurement
                {
                    Old = 200
                }
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""value"":""off"",
                    ""default"":""off""
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""new"": true,
                            ""old"": true
                        }
                    },
                    {
                        ""key"": ""latency_ms"",
                        ""values"": {
                            ""old"": 200
                        }
                    }
                ]
            }"));
        }

        [Fact]
        public void ItCanHandleOnlyNewLatency()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                Value = LdValue.Of("off"),
                Default = LdValue.Of("off"),
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    New = true,
                    Old = true
                },
                Latency = new MigrationOpEvent.LatencyMeasurement
                {
                    New = 200
                }
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""value"":""off"",
                    ""default"":""off""
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""new"": true,
                            ""old"": true
                        }
                    },
                    {
                        ""key"": ""latency_ms"",
                        ""values"": {
                            ""new"": 200
                        }
                    }
                ]
            }"));
        }

        [Fact]
        public void ItCanHandleOnlyOldError()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                Value = LdValue.Of("off"),
                Default = LdValue.Of("off"),
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    New = true,
                    Old = true
                },
                Error = new MigrationOpEvent.ErrorMeasurement
                {
                    Old = true
                }
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""value"":""off"",
                    ""default"":""off""
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""new"": true,
                            ""old"": true
                        }
                    },
                    {
                        ""key"": ""error"",
                        ""values"": {
                            ""old"": true
                        }
                    }
                ]
            }"));
        }

        [Fact]
        public void ItCanHandleOnlyNewError()
        {
            var context = Context.New(ContextKind.Of("user"), "userKey");
            var migrationOpEvent = new MigrationOpEvent
            {
                Timestamp = UnixMillisecondTime.OfMillis(100),
                Context = context,
                Operation = "read",
                SamplingRatio = 2,
                // Evaluation detail
                FlagKey = "my-migration",
                Value = LdValue.Of("off"),
                Default = LdValue.Of("off"),
                // Measurements
                Invoked = new MigrationOpEvent.InvokedMeasurement
                {
                    New = true,
                    Old = true
                },
                Error = new MigrationOpEvent.ErrorMeasurement
                {
                    New = true
                }
            };

            TestEventSerialization(migrationOpEvent, LdValue.Parse(@"{
                ""kind"":""migration_op"",
                ""creationDate"":100,
                ""samplingRatio"": 2,
                ""context"": {""kind"":""user"", ""key"":""userKey""},
                ""operation"": ""read"",
                ""evaluation"": {
                    ""key"":""my-migration"",
                    ""value"":""off"",
                    ""default"":""off""
                },
                ""measurements"": [
                    {
                        ""key"": ""invoked"",
                        ""values"": {
                            ""new"": true,
                            ""old"": true
                        }
                    },
                    {
                        ""key"": ""error"",
                        ""values"": {
                            ""new"": true
                        }
                    }
                ]
            }"));
        }


        private LdValue SerializeOneEvent(EventOutputFormatter f, object e)
        {
            var json = f.SerializeOutputEvents(new object[] {e}, new EventSummary[0], out var count);
            var outputEvent = TestUtil.TryParseJson(json).Get(0);
            Assert.Equal(1, count);
            return outputEvent;
        }

        private void TestEventSerialization(object e, LdValue expectedJsonValue)
        {
            var f = new EventOutputFormatter(new EventsConfiguration());
            var outputEvent = SerializeOneEvent(f, e);
            Assert.Equal(expectedJsonValue, outputEvent);
        }
    }
}
