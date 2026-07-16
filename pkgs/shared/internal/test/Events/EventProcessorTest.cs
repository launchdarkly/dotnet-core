using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Json;
using Moq;
using Xunit;

using static LaunchDarkly.Sdk.Internal.Events.EventTypes;
using static LaunchDarkly.Sdk.TestUtil;
using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventProcessorTest
    {
        private static readonly UnixMillisecondTime _fixedTimestamp = UnixMillisecondTime.OfMillis(10000);

        private static readonly Context _context = Context.Builder("userKey").Name("Red").Build();

        private static readonly EvaluationReason _irrelevantReason = EvaluationReason.OffReason;

        private static TestFlagProperties BasicFlag => new TestFlagProperties
        {
            Key = "flagkey",
            Version = 11
        };

        private static TestFlagProperties BasicFlagWithTracking => new TestFlagProperties
        {
            Key = "flagkey",
            Version = 11,
            TrackEvents = true
        };

        private static TestEvalProperties BasicEval => new TestEvalProperties
        {
            Timestamp = _fixedTimestamp,
            Context = _context,
            Variation = 1,
            Value = LdValue.Of("value")
        };

        private static TestCustomEventProperties BasicCustom => new TestCustomEventProperties
        {
            Timestamp = _fixedTimestamp,
            Context = _context,
            Key = "eventkey",
            Data = LdValue.Of(3)
        };

        private EventsConfiguration _config = new EventsConfiguration();
        private readonly LdValue _contextJson = LdValue.Parse("{\"kind\":\"user\",\"key\":\"userKey\",\"name\":\"Red\"}");

        public EventProcessorTest()
        {
            _config.EventCapacity = 100;
            _config.EventFlushInterval = TimeSpan.FromMilliseconds(-1);
            _config.DiagnosticRecordingInterval = TimeSpan.FromMinutes(5);
        }

        private Mock<IEventSender> MakeMockSender()
        {
            var mockSender = new Mock<IEventSender>(MockBehavior.Strict);
            mockSender.Setup(s => s.Dispose());
            return mockSender;
        }

        private Mock<IDiagnosticStore> MakeDiagnosticStore(
            DiagnosticEvent? persistedUnsentEvent,
            DiagnosticEvent? initEvent,
            DiagnosticEvent statsEvent
        )
        {
            var mockDiagnosticStore = new Mock<IDiagnosticStore>(MockBehavior.Strict);
            mockDiagnosticStore.Setup(diagStore => diagStore.PersistedUnsentEvent).Returns(persistedUnsentEvent);
            mockDiagnosticStore.Setup(diagStore => diagStore.InitEvent).Returns(initEvent);
            mockDiagnosticStore.Setup(diagStore => diagStore.DataSince).Returns(DateTime.Now);
            mockDiagnosticStore.Setup(diagStore => diagStore.RecordEventsInBatch(It.IsAny<long>()));
            mockDiagnosticStore.Setup(diagStore => diagStore.CreateEventAndReset()).Returns(statsEvent);
            return mockDiagnosticStore;
        }

        private EventProcessor MakeProcessor(EventsConfiguration config, Mock<IEventSender> mockSender)
        {
            return MakeProcessor(config, mockSender, null, null, null);
        }

        private EventProcessor MakeProcessor(EventsConfiguration config, Mock<IEventSender> mockSender,
            IDiagnosticStore diagnosticStore, IDiagnosticDisabler diagnosticDisabler, CountdownEvent diagnosticCountdown)
        {
            return new EventProcessor(config, mockSender.Object, new TestContextDeduplicator(),
                diagnosticStore, diagnosticDisabler, NullLogger, () => { diagnosticCountdown.Signal(); });
        }

        private void RecordEval(EventProcessor ep, TestFlagProperties f, TestEvalProperties e)
        {
            ep.RecordEvaluationEvent(new EvaluationEvent
            {
                Timestamp = e.Timestamp,
                Context = e.Context,
                FlagKey = f.Key,
                FlagVersion = f.Version,
                Variation = e.Variation,
                Value = e.Value,
                Default = e.DefaultValue,
                Reason = e.Reason,
                PrereqOf = e.PrereqOf,
                TrackEvents = f.TrackEvents,
                DebugEventsUntilDate = f.DebugEventsUntilDate
            });
        }

        private void RecordIdentify(EventProcessor ep, UnixMillisecondTime time, Context context) =>
            ep.RecordIdentifyEvent(new IdentifyEvent { Timestamp = time, Context = context});

        private void RecordCustom(EventProcessor ep, TestCustomEventProperties e)
        {
            ep.RecordCustomEvent(new CustomEvent
            {
                Timestamp = e.Timestamp,
                Context = e.Context,
                EventKey = e.Key,
                Data = e.Data,
                MetricValue = e.MetricValue
            });
        }

        private void FlushAndWait(EventProcessor ep, EventCapture captured)
        {
            ep.Flush();
            captured.AwaitPayload();
        }

        [Fact]
        public void FlushingNoEventsCompletes()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                var start = DateTime.Now;
                // This should complete immediately. If it doesn't then that is a problem.
                // If this is broken, and you do not provide a timeout, then it could wait forever.
                ep.FlushAndWait(TimeSpan.FromSeconds(10));
                var diff = DateTime.Now - start;
                if (diff.Seconds > 5)
                {
                    Assert.Fail("Flushing without events did not complete immediately.");
                }
                Assert.Empty(captured.Events);
            }
        }

        [Fact]
        public void IdentifyEventIsQueued()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIdentifyEvent(item, _fixedTimestamp, _contextJson));
            }
        }

        [Fact]
        public void IndividualFeatureEventIsQueuedWithIndexEvent()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordEval(ep, BasicFlagWithTracking, BasicEval);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, BasicEval.Timestamp, _contextJson),
                    item => CheckFeatureEvent(item, BasicFlagWithTracking, BasicEval, _contextJson),
                    item => CheckSummaryEvent(item));
            }
        }

        [Fact]
        public void ItDoesNotQueueAFeatureEventWithSamplingRatioOfZero()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                ep.RecordEvaluationEvent(new EvaluationEvent
                {
                    FlagKey =  "the-flag",
                    Value = LdValue.Of("value"),
                    Default = LdValue.Of("default"),
                    SamplingRatio = 0,
                    Context = _context,
                    Timestamp = BasicEval.Timestamp
                });
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, BasicEval.Timestamp, _contextJson),
                    item => CheckSummaryEvent(item));
            }
        }

        [Fact]
        public void ItDoesNotSummarizeAFeatureEventThatIsExcludedFromSummaries()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                ep.RecordEvaluationEvent(new EvaluationEvent
                {
                    FlagKey =  "the-flag",
                    Value = LdValue.Of("value"),
                    Default = LdValue.Of("default"),
                    SamplingRatio = 0,
                    ExcludeFromSummaries = true,
                    Context = _context,
                    Timestamp = BasicEval.Timestamp
                });
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, BasicEval.Timestamp, _contextJson));
            }
        }

        [Fact]
        public void FeatureEventCanHaveReason()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                var reasons = new EvaluationReason[]
                {
                    _irrelevantReason,
                    EvaluationReason.FallthroughReason,
                    EvaluationReason.TargetMatchReason,
                    EvaluationReason.RuleMatchReason(1, "id"),
                    EvaluationReason.PrerequisiteFailedReason("key"),
                    EvaluationReason.ErrorReason(EvaluationErrorKind.WrongType)
                };
                var userCounter = 0;
                foreach (var reason in reasons)
                {
                    captured.Events.Clear();

                    var userKey = "user" + (++userCounter);
                    var contextJson = LdValue.Parse("{\"kind\":\"user\",\"key\":\"" + userKey + "\"}");
                    var context = Context.New(userKey);
                    var eval = BasicEval;
                    eval.Context = context;
                    eval.Reason = reason;
                    RecordEval(ep, BasicFlagWithTracking, eval);
                    FlushAndWait(ep, captured);

                    Assert.Collection(captured.Events,
                        item => CheckIndexEvent(item),
                        item => CheckFeatureEvent(item, BasicFlagWithTracking, eval, contextJson),
                        item => CheckSummaryEvent(item));
                }
            }
        }

        [Fact]
        public void EventKindIsDebugIfFlagIsTemporarilyInDebugMode()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                var flag = BasicFlag;
                flag.DebugEventsUntilDate = UnixMillisecondTime.Now.PlusMillis(1000000);
                RecordEval(ep, flag, BasicEval);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, BasicEval.Timestamp, _contextJson),
                    item => CheckDebugEvent(item, flag, BasicEval, _contextJson),
                    item => CheckSummaryEvent(item));
            }
        }

        [Fact]
        public void EventCanBeBothTrackedAndDebugged()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                var flag = BasicFlagWithTracking;
                flag.DebugEventsUntilDate = UnixMillisecondTime.Now.PlusMillis(1000000);
                RecordEval(ep, flag, BasicEval);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, BasicEval.Timestamp, _contextJson),
                    item => CheckFeatureEvent(item, flag, BasicEval, _contextJson),
                    item => CheckDebugEvent(item, flag, BasicEval, _contextJson),
                    item => CheckSummaryEvent(item));
            }
        }

        [Fact]
        public void DebugModeExpiresBasedOnClientTimeIfClientTimeIsLaterThanServerTime()
        {
            // Pick a server time that is somewhat behind the client time
            var serverTime = DateTime.Now.Subtract(TimeSpan.FromSeconds(20));

            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender,
                new EventSenderResult(DeliveryStatus.Succeeded, serverTime));

            using (var ep = MakeProcessor(_config, mockSender))
            {
                // Send and flush an event we don't care about, just to set the last server time
                RecordIdentify(ep, _fixedTimestamp, Context.New("otherUser"));
                FlushAndWait(ep, captured);
                captured.Events.Clear();
                ep.WaitUntilInactive(); // this waits till flush tasks have completed, so the time has been updated from the response

                // Now send an event with debug mode on, with a "debug until" time that is further in
                // the future than the server time, but in the past compared to the client.
                var flag = BasicFlag;
                flag.DebugEventsUntilDate = UnixMillisecondTime.FromDateTime(serverTime).PlusMillis(1000);
                RecordEval(ep, flag, BasicEval);
                FlushAndWait(ep, captured);

                // Should get a summary event only, not a full feature event
                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, BasicEval.Timestamp, _contextJson),
                    item => CheckSummaryEvent(item));
            }
        }

        [Fact]
        public void DebugModeExpiresBasedOnServerTimeIfServerTimeIsLaterThanClientTime()
        {
            // Pick a server time that is somewhat ahead of the client time
            var serverTime = DateTime.Now.Add(TimeSpan.FromSeconds(20));

            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender,
                new EventSenderResult(DeliveryStatus.Succeeded, serverTime));

            using (var ep = MakeProcessor(_config, mockSender))
            {
                // Send and flush an event we don't care about, just to set the last server time
                RecordIdentify(ep, _fixedTimestamp, Context.New("otherUser"));
                FlushAndWait(ep, captured);
                captured.Events.Clear();
                ep.WaitUntilInactive(); // this waits till flush tasks have completed, so the time has been updated from the response

                // Now send an event with debug mode on, with a "debug until" time that is further in
                // the future than the client time, but in the past compared to the server.
                var flag = BasicFlag;
                flag.DebugEventsUntilDate = UnixMillisecondTime.FromDateTime(serverTime).PlusMillis(-1000);
                RecordEval(ep, flag, BasicEval);
                FlushAndWait(ep, captured);

                // Should get a summary event only, not a full feature event
                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, BasicEval.Timestamp, _contextJson),
                    item => CheckSummaryEvent(item));
            }
        }

        [Fact]
        public void TwoFeatureEventsForSameUserGenerateOnlyOneIndexEvent()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                var flag1 = new TestFlagProperties { Key = "flagkey1", Version = 11, TrackEvents = true };
                var flag2 = new TestFlagProperties { Key = "flagkey2", Version = 22, TrackEvents = true };
                var value = LdValue.Of("value");
                RecordEval(ep, flag1, BasicEval);
                RecordEval(ep, flag2, BasicEval);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, BasicEval.Timestamp, _contextJson),
                    item => CheckFeatureEvent(item, flag1, BasicEval, _contextJson),
                    item => CheckFeatureEvent(item, flag2, BasicEval, _contextJson),
                    item => CheckSummaryEvent(item));
            }
        }

        [Fact]
        public void NonTrackedEventsAreSummarized()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                var flag1 = new TestFlagProperties { Key = "flagkey1", Version = 11 };
                var flag2 = new TestFlagProperties { Key = "flagkey2", Version = 22 };
                var value1 = LdValue.Of("value1");
                var value2 = LdValue.Of("value2");
                var default1 = LdValue.Of("default1");
                var default2 = LdValue.Of("default2");
                var earliestTime = UnixMillisecondTime.OfMillis(10000);
                var latestTime = UnixMillisecondTime.OfMillis(20000);
                RecordEval(ep, flag1, new TestEvalProperties
                {
                    Timestamp = earliestTime,
                    Context = _context,
                    Variation = 1,
                    Value = value1,
                    DefaultValue = default1
                });
                RecordEval(ep, flag1, new TestEvalProperties
                {
                    Timestamp = UnixMillisecondTime.OfMillis(earliestTime.Value + 10),
                    Context = _context,
                    Variation = 1,
                    Value = value1,
                    DefaultValue = default1
                });
                RecordEval(ep, flag1, new TestEvalProperties
                {
                    Timestamp = UnixMillisecondTime.OfMillis(earliestTime.Value + 20),
                    Context = _context,
                    Variation = 2,
                    Value = value2,
                    DefaultValue = default1
                });
                RecordEval(ep, flag2, new TestEvalProperties
                {
                    Timestamp = latestTime,
                    Context = _context,
                    Variation = 2,
                    Value = value2,
                    DefaultValue = default2
                });
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, earliestTime, _contextJson),
                    item => CheckSummaryEventDetails(item,
                        earliestTime,
                        latestTime,
                        MustHaveFlagSummary(flag1.Key, default1,
                            MustHaveFlagSummaryCounter(value1, 1, flag1.Version, 2),
                            MustHaveFlagSummaryCounter(value2, 2, flag1.Version, 1)
                        ),
                        MustHaveFlagSummary(flag2.Key, default2,
                            MustHaveFlagSummaryCounter(value2, 2, flag2.Version, 1)
                        )
                    )
                );
            }
        }

        [Fact]
        public void CustomEventIsQueuedWithUser()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                var ce = new TestCustomEventProperties
                {
                    Timestamp = _fixedTimestamp,
                    Context = _context,
                    Key = "eventkey",
                    Data = LdValue.Of(3),
                    MetricValue = 1.5
                };
                RecordCustom(ep, ce);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIndexEvent(item, ce.Timestamp, _contextJson),
                    item => CheckCustomEvent(item, ce, _contextJson));
            }
        }

        [Fact]
        public void AsyncFlushWithNoWait()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);
            captured.Delay = TimeSpan.FromMilliseconds(200);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);
                ep.Flush();

                Assert.Empty(captured.Events); // Flush() returns before flush has actually happened

                captured.AwaitPayload(); // but it does happen if we wait
                Assert.Collection(captured.Events,
                    item => CheckIdentifyEvent(item, _fixedTimestamp, _contextJson));
            };
        }

        [Fact]
        public void FlushAndWaitSucceeds()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);
            captured.Delay = TimeSpan.FromMilliseconds(100);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);
                bool sent = ep.FlushAndWait(TimeSpan.FromMilliseconds(500)); // allow for random execution delays

                Assert.True(sent);
                Assert.Collection(captured.Events,
                    item => CheckIdentifyEvent(item, _fixedTimestamp, _contextJson));
            };
        }

        [Fact]
        public void FlushAndWaitTimesOut()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);
            captured.Delay = TimeSpan.FromMilliseconds(200);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);
                bool sent = ep.FlushAndWait(TimeSpan.FromMilliseconds(10));

                Assert.False(sent);
                Assert.Empty(captured.Events); // flush hasn't happened yet
                captured.AwaitPayload(); // but it does happen if we wait
            };
        }

        [Fact]
        public async Task AsyncFlushAndWaitSucceeds()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);
            captured.Delay = TimeSpan.FromMilliseconds(100);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);
                bool sent = await ep.FlushAndWaitAsync(TimeSpan.FromMilliseconds(500)); // allow for random execution delays

                Assert.True(sent);
                Assert.Collection(captured.Events,
                    item => CheckIdentifyEvent(item, _fixedTimestamp, _contextJson));
            };
        }

        [Fact]
        public async Task AsyncFlushAndWaitTimesOut()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);
            captured.Delay = TimeSpan.FromMilliseconds(200);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);
                bool sent = await ep.FlushAndWaitAsync(TimeSpan.FromMilliseconds(10));

                Assert.False(sent);
                Assert.Empty(captured.Events); // flush hasn't happened yet
                captured.AwaitPayload(); // but it does happen if we wait
            };
        }

        [Fact]
        public void FinalFlushIsDoneOnDispose()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);

                ep.Dispose();

                Assert.Collection(captured.Events,
                    item => CheckIdentifyEvent(item, _fixedTimestamp, _contextJson));
                mockSender.Verify(s => s.Dispose(), Times.Once());
            }
        }

        [Fact]
        public void FlushDoesNothingIfThereAreNoEvents()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                ep.Flush();
                captured.ExpectNoMorePayloads();
            }
        }

        [Fact]
        public void FlushDoesNothingWhenOffline()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                ep.SetOffline(true);
                RecordIdentify(ep, _fixedTimestamp, _context);
                ep.Flush();
                captured.ExpectNoMorePayloads();

                // We should have still held on to that event, so if we go online again and flush, it is sent.
                ep.SetOffline(false);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIdentifyEvent(item, _fixedTimestamp, _contextJson));
            };
        }

        [Fact]
        public void EventsAreStillPostedAfterRecoverableFailure()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender,
                new EventSenderResult(DeliveryStatus.Failed, null));

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIdentifyEvent(item, _fixedTimestamp, _contextJson));
                captured.Events.Clear();

                RecordCustom(ep, BasicCustom);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckCustomEvent(item, BasicCustom, _contextJson));
            }
        }

        [Fact]
        public void EventsAreNotPostedAfterUnrecoverableFailure()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender,
                new EventSenderResult(DeliveryStatus.FailedAndMustShutDown, null));

            using (var ep = MakeProcessor(_config, mockSender))
            {
                RecordIdentify(ep, _fixedTimestamp, _context);
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events,
                    item => CheckIdentifyEvent(item, _fixedTimestamp, _contextJson));

                RecordCustom(ep, BasicCustom);
                ep.Flush();
                captured.ExpectNoMorePayloads();
            }
        }

        [Fact]
        public void EventsInBatchRecorded()
        {
            var expectedStats = LdValue.BuildObject().Add("stats", "testValue").Build();
            var mockDiagnosticStore = MakeDiagnosticStore(null, null, new DiagnosticEvent(expectedStats));

            var mockSender = MakeMockSender();
            var eventCapture = EventCapture.From(mockSender);
            var diagnosticCapture = EventCapture.DiagnosticsFrom(mockSender);
            CountdownEvent diagnosticCountdown = new CountdownEvent(1);

            using (var ep = MakeProcessor(_config, mockSender, mockDiagnosticStore.Object, null, diagnosticCountdown))
            {
                RecordEval(ep, BasicFlagWithTracking, BasicEval);
                FlushAndWait(ep, eventCapture);

                mockDiagnosticStore.Verify(diagStore => diagStore.RecordEventsInBatch(2), Times.Once(),
                    "Diagnostic store's RecordEventsInBatch should be called with the number of events in last flush");

                ep.DoDiagnosticSend(null);
                diagnosticCountdown.Wait();
                mockDiagnosticStore.Verify(diagStore => diagStore.CreateEventAndReset(), Times.Once());

                Assert.Equal(expectedStats, diagnosticCapture.EventsQueue.Take());
            }
        }

        [Fact]
        public void DiagnosticStorePersistedUnsentEventSentToDiagnosticUri()
        {
            var expected = LdValue.BuildObject().Add("testKey", "testValue").Build();
            var mockDiagnosticStore = MakeDiagnosticStore(new DiagnosticEvent(expected), null,
                new DiagnosticEvent(LdValue.Null));

            var mockSender = MakeMockSender();
            var eventCapture = EventCapture.From(mockSender);
            var diagnosticCapture = EventCapture.DiagnosticsFrom(mockSender);
            var diagnosticCountdown = new CountdownEvent(1);

            using (var ep = MakeProcessor(_config, mockSender, mockDiagnosticStore.Object, null, diagnosticCountdown))
            {
                diagnosticCountdown.Wait();

                Assert.Equal(expected, diagnosticCapture.EventsQueue.Take());
            }
        }

        [Fact]
        public void DiagnosticStoreInitEventSentToDiagnosticUri()
        {
            var expected = LdValue.BuildObject().Add("testKey", "testValue").Build();
            var mockDiagnosticStore = MakeDiagnosticStore(null, new DiagnosticEvent(expected),
                new DiagnosticEvent(LdValue.Null));

            var mockSender = MakeMockSender();
            var eventCapture = EventCapture.From(mockSender);
            var diagnosticCapture = EventCapture.DiagnosticsFrom(mockSender);
            var diagnosticCountdown = new CountdownEvent(1);

            using (var ep = MakeProcessor(_config, mockSender, mockDiagnosticStore.Object, null, diagnosticCountdown))
            {
                diagnosticCountdown.Wait();

                Assert.Equal(expected, diagnosticCapture.EventsQueue.Take());
            }
        }

        [Fact]
        public void DiagnosticDisablerDisablesInitialDiagnostics()
        {
            var testDiagnostic = LdValue.BuildObject().Add("testKey", "testValue").Build();
            var mockDiagnosticStore = MakeDiagnosticStore(new DiagnosticEvent(testDiagnostic),
                new DiagnosticEvent(testDiagnostic), new DiagnosticEvent(LdValue.Null));

            var mockDiagnosticDisabler = new Mock<IDiagnosticDisabler>(MockBehavior.Strict);
            mockDiagnosticDisabler.Setup(diagDisabler => diagDisabler.Disabled).Returns(true);

            var mockSender = MakeMockSender();
            var eventCapture = EventCapture.From(mockSender);
            var diagnosticCapture = EventCapture.DiagnosticsFrom(mockSender);

            using (var ep = MakeProcessor(_config, mockSender, mockDiagnosticStore.Object, mockDiagnosticDisabler.Object, null))
            {
            }
            mockDiagnosticStore.Verify(diagStore => diagStore.InitEvent, Times.Never());
            mockDiagnosticStore.Verify(diagStore => diagStore.PersistedUnsentEvent, Times.Never());
            Assert.Empty(diagnosticCapture.Events);
        }

        [Fact]
        public void DiagnosticDisablerEnabledInitialDiagnostics()
        {
            var expectedStats = LdValue.BuildObject().Add("stats", "testValue").Build();
            var expectedInit = LdValue.BuildObject().Add("init", "testValue").Build();
            var mockDiagnosticStore = MakeDiagnosticStore(new DiagnosticEvent(expectedStats),
                new DiagnosticEvent(expectedInit), new DiagnosticEvent(LdValue.Null));

            var mockDiagnosticDisabler = new Mock<IDiagnosticDisabler>(MockBehavior.Strict);
            mockDiagnosticDisabler.Setup(diagDisabler => diagDisabler.Disabled).Returns(false);

            var mockSender = MakeMockSender();
            var eventCapture = EventCapture.From(mockSender);
            var diagnosticCapture = EventCapture.DiagnosticsFrom(mockSender);
            var diagnosticCountdown = new CountdownEvent(1);

            using (var ep = MakeProcessor(_config, mockSender, mockDiagnosticStore.Object, mockDiagnosticDisabler.Object, diagnosticCountdown))
            {
                diagnosticCountdown.Wait();

                Assert.Equal(expectedStats, diagnosticCapture.EventsQueue.Take());
                Assert.Equal(expectedInit, diagnosticCapture.EventsQueue.Take());
            }
        }

        [Fact]
        public void ItCanQueueAMigrationOpEvent()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                ep.RecordMigrationOpEvent(new MigrationOpEvent
                {
                    Context = _context,
                    FlagKey = "flag-key",
                    Value = LdValue.Of("live"),
                    Default =  LdValue.Of("off"),
                    SamplingRatio = 1,
                    Invoked = new MigrationOpEvent.InvokedMeasurement
                    {
                        New = true
                    }
                });
                FlushAndWait(ep, captured);

                Assert.Collection(captured.Events, AssertMigrationEvent);
            }
        }

        [Fact]
        public void ItDoesNotQueueAMigrationOpEventWithASamplingRatioOfZero()
        {
            var mockSender = MakeMockSender();
            var captured = EventCapture.From(mockSender);

            using (var ep = MakeProcessor(_config, mockSender))
            {
                ep.RecordMigrationOpEvent(new MigrationOpEvent
                {
                    Context = _context,
                    FlagKey = "flag-key",
                    Value = LdValue.Of("live"),
                    Default =  LdValue.Of("off"),
                    SamplingRatio = 0,
                    Invoked = new MigrationOpEvent.InvokedMeasurement
                    {
                        New = true
                    }
                });

                Assert.True(ep.FlushAndWait(TimeSpan.Zero));
                captured.ExpectNoMorePayloads();
                Assert.Empty(captured.Events);
            }
        }

        private void AssertMigrationEvent(LdValue item)
        {
            // For most tests we just need to check that there is a migration event.
            // The output formatting test verify the contents of the events.
            Assert.Equal("migration_op", item.Get("kind").AsString);
        }


        private void CheckIdentifyEvent(LdValue t, UnixMillisecondTime timestamp, LdValue contextJson) =>
            AssertJsonEqual(
                LdValue.BuildObject().Set("kind", "identify").
                    Set("creationDate", timestamp.Value).
                    Set("context", contextJson).Build().ToJsonString(),
                t.ToJsonString());

        private void CheckIndexEvent(LdValue t)
        {
            Assert.Equal(LdValue.Of("index"), t.Get("kind"));
        }

        private void CheckIndexEvent(LdValue t, UnixMillisecondTime timestamp, LdValue contextJson) =>
            AssertJsonEqual(
                LdValue.BuildObject().Set("kind", "index").
                    Set("creationDate", timestamp.Value).
                    Set("context", contextJson).Build().ToJsonString(),
                t.ToJsonString());

        private void CheckFeatureEvent(LdValue t, TestFlagProperties f, TestEvalProperties e, LdValue userJson)
        {
            CheckFeatureOrDebugEvent("feature", t, f, e, userJson);
        }

        private void CheckDebugEvent(LdValue t, TestFlagProperties f, TestEvalProperties e,
            LdValue userJson)
        {
            CheckFeatureOrDebugEvent("debug", t, f, e, userJson);
        }

        private void CheckFeatureOrDebugEvent(string kind, LdValue t, TestFlagProperties f, TestEvalProperties e,
            LdValue userJson)
        {
            Assert.Equal(LdValue.Of(kind), t.Get("kind"));
            Assert.Equal(LdValue.Of(e.Timestamp.Value), t.Get("creationDate"));
            Assert.Equal(LdValue.Of(f.Key), t.Get("key"));
            Assert.Equal(LdValue.Of(f.Version), t.Get("version"));
            Assert.Equal(e.Variation.HasValue ? LdValue.Of(e.Variation.Value) : LdValue.Null, t.Get("variation"));
            Assert.Equal(e.Value, t.Get("value"));
            Assert.Equal(userJson, t.Get("context"));
            Assert.Equal(e.Reason.HasValue ?
                LdValue.Parse(LdJsonSerialization.SerializeObject(e.Reason.Value)) : LdValue.Null,
                t.Get("reason"));
        }

        private void CheckCustomEvent(LdValue t, TestCustomEventProperties e, LdValue contextJson)
        {
            Assert.Equal(LdValue.Of("custom"), t.Get("kind"));
            Assert.Equal(LdValue.Of(e.Key), t.Get("key"));
            Assert.Equal(e.Data, t.Get("data"));
            Assert.Equal(contextJson, t.Get("context"));
            Assert.Equal(e.MetricValue.HasValue ? LdValue.Of(e.MetricValue.Value) : LdValue.Null, t.Get("metricValue"));
        }

        private void CheckSummaryEvent(LdValue t)
        {
            Assert.Equal(LdValue.Of("summary"), t.Get("kind"));
        }

        private void CheckSummaryEventDetails(LdValue o, UnixMillisecondTime startDate, UnixMillisecondTime endDate, params Action<LdValue>[] flagChecks)
        {
            CheckSummaryEvent(o);
            Assert.Equal(LdValue.Of(startDate.Value), o.Get("startDate"));
            Assert.Equal(LdValue.Of(endDate.Value), o.Get("endDate"));
            var features = o.Get("features");
            Assert.Equal(flagChecks.Length, features.Count);
            foreach (var flagCheck in flagChecks)
            {
                flagCheck(features);
            }
        }

        private Action<LdValue> MustHaveFlagSummary(string flagKey, LdValue defaultVal, params Action<string, LdValue>[] counterChecks)
        {
            return o =>
            {
                var fo = o.Get(flagKey);
                if (fo.IsNull)
                {
                    Assert.True(false, "could not find flag '" + flagKey + "' in: " + fo.ToString());
                }
                LdValue cs = fo.Get("counters");
                Assert.True(defaultVal.Equals(fo.Get("default")),
                    "default should be " + defaultVal + " in " + fo);
                if (counterChecks.Length != cs.Count)
                {
                    Assert.True(false, "number of counters should be " + counterChecks.Length + " in " + fo + " for flag " + flagKey);
                }
                foreach (var counterCheck in counterChecks)
                {
                    counterCheck(flagKey, cs);
                }
            };
        }

        private Action<string, LdValue> MustHaveFlagSummaryCounter(LdValue value, int? variation, int? version, int count)
        {
            return (flagKey, items) =>
            {
                if (!items.AsList(LdValue.Convert.Json).Any(o =>
                {
                    return o.Get("value").Equals(value)
                        && o.Get("version").Equals(version.HasValue ? LdValue.Of(version.Value) : LdValue.Null)
                        && o.Get("variation").Equals(variation.HasValue ? LdValue.Of(variation.Value) : LdValue.Null)
                        && o.Get("count").Equals(LdValue.Of(count));
                }))
                {
                    Assert.True(false, "could not find counter for (" + value + ", " + version + ", " + variation + ", " + count
                        + ") in: " + items.ToString() + " for flag " + flagKey);
                }
            };
        }
    }

    class EventCapture
    {
        public readonly List<LdValue> Events = new List<LdValue>();
        public readonly BlockingCollection<LdValue> Payloads = new BlockingCollection<LdValue>();
        public readonly BlockingCollection<LdValue> EventsQueue = new BlockingCollection<LdValue>();

        public TimeSpan? Delay;

        public LdValue AwaitPayload(TimeSpan? timeout = null)
        {
            if (!Payloads.TryTake(out var p, timeout ?? TimeSpan.FromSeconds(5)))
            {
                throw new Exception("timed out waiting for event payload");
            }
            return p;
        }

        public void ExpectNoMorePayloads(TimeSpan? timeout = null)
        {
            if (Payloads.TryTake(out _, timeout ?? TimeSpan.FromMilliseconds(100)))
            {
                throw new Exception("received unexpected event payload");
            }
        }

        internal static EventCapture From(Mock<IEventSender> mockSender) =>
            From(mockSender, new EventSenderResult(DeliveryStatus.Succeeded, null));

        internal static EventCapture From(Mock<IEventSender> mockSender, EventSenderResult result) =>
            From(mockSender, EventDataKind.AnalyticsEvents, result);

        internal static EventCapture DiagnosticsFrom(Mock<IEventSender> mockSender) =>
            From(mockSender, EventDataKind.DiagnosticEvent, new EventSenderResult(DeliveryStatus.Succeeded, null));

        internal static EventCapture From(Mock<IEventSender> mockSender, EventDataKind forKind, EventSenderResult result)
        {
            var ec = new EventCapture();
            mockSender.Setup(
                s => s.SendEventDataAsync(forKind, It.IsAny<byte[]>(), It.IsAny<int>())
            ).Callback<EventDataKind, byte[], int>((kind, data, count) =>
            {
                if (ec.Delay.HasValue)
                {
                    Thread.Sleep(ec.Delay.Value);
                }
                var parsed = TestUtil.TryParseJson(data);
                var events = kind == EventDataKind.DiagnosticEvent ? new List<LdValue> { parsed } :
                    parsed.AsList(LdValue.Convert.Json);
                ec.Events.AddRange(events);
                foreach (var e in events)
                {
                    ec.EventsQueue.Add(e);
                }
                ec.Payloads.Add(parsed);
            }).Returns(Task.FromResult(result));
            return ec;
        }
    }

    class TestContextDeduplicator : IContextDeduplicator
    {
        private HashSet<string> _contextKeys = new HashSet<string>();
        public TimeSpan? FlushInterval => null;

        public void Flush()
        {
        }

        public bool ProcessContext(in Context context)
        {
            if (!_contextKeys.Contains(context.FullyQualifiedKey))
            {
                _contextKeys.Add(context.FullyQualifiedKey);
                return true;
            }
            return false;
        }
    }
}
