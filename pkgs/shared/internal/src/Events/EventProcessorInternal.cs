using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;

using static LaunchDarkly.Sdk.Internal.Events.EventTypes;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal class EventProcessorInternal
    {
        #region Inner types

        // These types are used only for communication between EventProcessor and
        // EventProcessorInternal on their shared queue.

        internal interface IEventMessage { }

        internal class EventMessage : IEventMessage
        {
            internal object Event { get; }

            internal EventMessage(object e)
            {
                Event = e;
            }
        }

        internal class FlushMessage : SynchronousMessage { }

        internal class FlushContextsMessage : IEventMessage { }

        internal class DiagnosticMessage : IEventMessage { }

        internal class SynchronousMessage : IEventMessage
        {
            internal readonly Semaphore _reply;
            internal readonly TaskCompletionSource<bool> _asyncReply;

            internal SynchronousMessage()
            {
                _reply = new Semaphore(0, 1);
                _asyncReply = new TaskCompletionSource<bool>();
            }

            internal void WaitForCompletion()
            {
                _reply.WaitOne();
            }

            internal bool WaitForCompletion(TimeSpan timeout)
            {
                if (timeout <= TimeSpan.Zero)
                {
                    WaitForCompletion();
                    return true;
                }
                return _reply.WaitOne(timeout);
            }

            internal Task<bool> WaitForCompletionAsync(TimeSpan timeout)
            {
                if (timeout <= TimeSpan.Zero)
                {
                    return _asyncReply.Task;
                }
                var timeoutTask = Task.Delay(timeout).ContinueWith(t => false);
                return Task.WhenAny(
                    _asyncReply.Task,
                    timeoutTask
                    ).Result;
            }

            internal void Completed()
            {
                _reply.Release();
                _asyncReply.TrySetResult(true);
            }
        }

        internal class TestSyncMessage : SynchronousMessage { }

        internal class ShutdownMessage : SynchronousMessage { }

        // DebugEvent and IndexEvent are defined here, instead of publicly in EventTypes, because they
        // are never passed in via the public API; they're only created internally by EventProcessorInternal.

        internal struct DebugEvent
        {
            public EvaluationEvent FromEvent;
        }

        internal struct IndexEvent
        {
            public UnixMillisecondTime Timestamp;
            public Context Context;
        }

        internal struct FlushPayload
        {
            internal object[] Events { get; set; }
            internal IReadOnlyList<EventSummary> Summaries { get; set; }
        }

        internal sealed class EventBuffer
        {
            private readonly List<object> _events;
            private readonly IEventSummarizer _summarizer;
            private readonly IDiagnosticStore _diagnosticStore;
            private readonly int _capacity;
            private readonly Logger _logger;
            private bool _exceededCapacity;

            internal EventBuffer(int capacity, bool perContextSummaries, IDiagnosticStore diagnosticStore, Logger logger)
            {
                _capacity = capacity;
                _events = new List<object>();
                _summarizer = perContextSummaries
                    ? (IEventSummarizer)new PerContextEventSummarizer()
                    : new AggregatedEventSummarizer();
                _diagnosticStore = diagnosticStore;
                _logger = logger;
            }

            internal void AddEvent(object e)
            {
                if (_events.Count >= _capacity)
                {
                    _diagnosticStore?.IncrementDroppedEvents();
                    if (!_exceededCapacity)
                    {
                        _logger.Warn("Exceeded event queue capacity. Increase capacity to avoid dropping events.");
                        _exceededCapacity = true;
                    }
                }
                else
                {
                    _events.Add(e);
                    _exceededCapacity = false;
                }
            }

            internal void AddToSummary(EvaluationEvent ee) =>
                _summarizer.SummarizeEvent(ee.Timestamp, ee.FlagKey, ee.FlagVersion, ee.Variation, ee.Value, ee.Default,
                    ee.Context);

            internal FlushPayload GetPayload() =>
                new FlushPayload { Events = _events.ToArray(), Summaries = _summarizer.GetSummariesAndReset() };

            internal void Clear()
            {
                _events.Clear();
                _summarizer.Clear();
            }
        }

        #endregion

        #region Private fields

        private static readonly int MaxFlushWorkers = 5;

        private readonly EventsConfiguration _config;
        private readonly IDiagnosticStore _diagnosticStore;
        private readonly IContextDeduplicator _contextDeduplicator;
        private readonly CountdownEvent _flushWorkersCounter;
        private readonly Action _testActionOnDiagnosticSend;
        private readonly IEventSender _eventSender;
        private readonly Logger _logger;
        private long _lastKnownPastTime;
        private volatile bool _disabled;

        #endregion

        #region Constructor

        internal EventProcessorInternal(
            EventsConfiguration config,
            BlockingCollection<IEventMessage> messageQueue,
            IEventSender eventSender,
            IContextDeduplicator contextDeduplicator,
            IDiagnosticStore diagnosticStore,
            Logger logger,
            Action testActionOnDiagnosticSend
            )
        {
            _config = config;
            _diagnosticStore = diagnosticStore;
            _contextDeduplicator = contextDeduplicator;
            _testActionOnDiagnosticSend = testActionOnDiagnosticSend;
            _flushWorkersCounter = new CountdownEvent(1);
            _eventSender = eventSender;
            _logger = logger;

            EventBuffer buffer = new EventBuffer(config.EventCapacity > 0 ? config.EventCapacity : 1,
                config.PerContextSummaries, _diagnosticStore, _logger);

            // Here we use TaskFactory.StartNew instead of Task.Run() because that allows us to specify the
            // LongRunning option. This option tells the task scheduler that the task is likely to hang on
            // to a thread for a long time, so it should consider growing the thread pool.
            Task.Factory.StartNew(
                () => RunMainLoop(messageQueue, buffer),
                TaskCreationOptions.LongRunning
                );
        }

        #endregion

        #region Public methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private methods

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _eventSender.Dispose();
            }
        }

        private void RunMainLoop(BlockingCollection<IEventMessage> messageQueue, EventBuffer buffer)
        {
            bool running = true;
            while (running)
            {
                try
                {
                    IEventMessage message = messageQueue.Take();
                    switch (message)
                    {
                        case EventMessage em:
                            ProcessEvent(em.Event, buffer);
                            break;
                        case FlushMessage fm:
                            StartFlush(buffer, fm);
                            break;
                        case FlushContextsMessage fm:
                            if (_contextDeduplicator != null)
                            {
                                _contextDeduplicator.Flush();
                            }
                            break;
                        case DiagnosticMessage dm:
                            SendAndResetDiagnostics(buffer);
                            break;
                        case TestSyncMessage tm:
                            WaitForFlushes();
                            tm.Completed();
                            break;
                        case ShutdownMessage sm:
                            WaitForFlushes();
                            running = false;
                            sm.Completed();
                            break;
                    }
                }
                catch (Exception e)
                {
                    LogHelpers.LogException(_logger, "Unexpected error in event dispatcher thread", e);
                }
            }
        }

        private void SendAndResetDiagnostics(EventBuffer buffer)
        {
            if (_diagnosticStore != null)
            {
                Task.Run(() => SendDiagnosticEventAsync(_diagnosticStore.CreateEventAndReset()));
            }
        }

        private void WaitForFlushes()
        {
            // Our CountdownEvent was initialized with a count of 1, so that's the lowest it can be at this point.
            _flushWorkersCounter.Signal(); // Drop the count to zero if there are no active flush tasks.
            _flushWorkersCounter.Wait();   // Wait until it is zero.
            _flushWorkersCounter.Reset(1);
        }

        private void ProcessEvent(object e, EventBuffer buffer)
        {
            if (_disabled)
            {
                return;
            }

            // Decide whether to add the event to the payload. Feature events may be added twice, once for
            // the event (if tracked) and once for debugging.
            bool willAddFullEvent = true;
            DebugEvent? debugEvent = null;
            UnixMillisecondTime timestamp;
            Context context = new Context();
            switch (e)
            {
                case EvaluationEvent ee:
                    if (!ee.ExcludeFromSummaries)
                    {
                        buffer.AddToSummary(ee); // only evaluation events go into the summarizer
                    }

                    timestamp = ee.Timestamp;
                    context = ee.Context;
                    var samplingRatio = ee.SamplingRatio ?? 1;
                    willAddFullEvent = ee.TrackEvents && Sampler.Sample(samplingRatio);
                    if (ShouldDebugEvent(ee) && Sampler.Sample(samplingRatio))
                    {
                        debugEvent = new DebugEvent { FromEvent = ee };
                    }
                    break;
                case IdentifyEvent ie:
                    timestamp = ie.Timestamp;
                    context = ie.Context;
                    break;
                case CustomEvent ce:
                    timestamp = ce.Timestamp;
                    context = ce.Context;
                    break;
                case MigrationOpEvent me:
                    if (Sampler.Sample(me.SamplingRatio))
                    {
                        buffer.AddEvent(e);
                    }

                    // Migration events do not need to generate index events, so we can just return here.
                    return;
                default:
                    timestamp = new UnixMillisecondTime();
                    break;
            }

            // Tell the context deduplicator, if any, about this user; this may produce an index event.
            // We only need to do this if there is *not* already going to be a full-fidelity event
            // containing an inline user.
            if (context.Defined && _contextDeduplicator != null)
            {
                bool needUserEvent = _contextDeduplicator.ProcessContext(context);
                if (needUserEvent && !(e is IdentifyEvent))
                {
                    IndexEvent ie = new IndexEvent { Timestamp = timestamp, Context = context };
                    buffer.AddEvent(ie);
                }
                else if (!(e is IdentifyEvent))
                {
                    _diagnosticStore?.IncrementDeduplicatedUsers();
                }
            }

            if (willAddFullEvent)
            {
                buffer.AddEvent(e);
            }
            if (debugEvent != null)
            {
                buffer.AddEvent(debugEvent);
            }
        }

        private bool ShouldDebugEvent(EvaluationEvent fe)
        {
            if (fe.DebugEventsUntilDate != null)
            {
                long lastPast = Interlocked.Read(ref _lastKnownPastTime);
                if (fe.DebugEventsUntilDate.Value.Value > lastPast &&
                    fe.DebugEventsUntilDate.Value.Value > UnixMillisecondTime.Now.Value)
                {
                    return true;
                }
            }
            return false;
        }

        // Grabs a snapshot of the current internal state, and starts a new task to send it to the server.
        private void StartFlush(EventBuffer buffer, SynchronousMessage message)
        {
            if (_disabled)
            {
                message.Completed();
                return;
            }
            FlushPayload payload = buffer.GetPayload();
            if (_diagnosticStore != null)
            {
                _diagnosticStore.RecordEventsInBatch(payload.Events.Length);
            }
            if (payload.Events.Length > 0 || payload.Summaries.Count > 0)
            {
                lock (_flushWorkersCounter)
                {
                    // Note that this counter will be 1, not 0, when there are no active flush workers.
                    // This is because a .NET CountdownEvent can't be reused without explicitly resetting
                    // it once it has gone to zero.
                    if (_flushWorkersCounter.CurrentCount >= MaxFlushWorkers + 1)
                    {
                        // We already have too many workers, so just leave the events as is
                        message.Completed();
                        return;
                    }
                    // We haven't hit the limit, we'll go ahead and start a flush task
                    _flushWorkersCounter.AddCount(1);
                }
                buffer.Clear();
                Task.Run(async () => {
                    try
                    {
                        await FlushEventsAsync(payload);
                    }
                    finally
                    {
                        _flushWorkersCounter.Signal();
                        message.Completed();
                    }
                });
            }
            else
            {
                // There are no events to flush. If we don't complete the message, then the async task may never
                // complete (if it had a non-zero positive timeout, then it would complete after the timeout).
                message.Completed();
            }
        }

        private async Task FlushEventsAsync(FlushPayload payload)
        {
            EventOutputFormatter formatter = new EventOutputFormatter(_config);
            byte[] jsonEvents;
            int eventCount;
            try
            {
                jsonEvents = formatter.SerializeOutputEvents(payload.Events, payload.Summaries, out eventCount);
            }
            catch (Exception e)
            {
                LogHelpers.LogException(_logger, "Error preparing events, will not send", e);
                return;
            }

            var result = await _eventSender.SendEventDataAsync(EventDataKind.AnalyticsEvents,
                jsonEvents, eventCount);
            if (result.Status == DeliveryStatus.FailedAndMustShutDown)
            {
                _disabled = true;
            }
            if (result.TimeFromServer.HasValue)
            {
                Interlocked.Exchange(ref _lastKnownPastTime,
                    UnixMillisecondTime.FromDateTime(result.TimeFromServer.Value).Value);
            }
        }

        internal async Task SendDiagnosticEventAsync(DiagnosticEvent diagnostic)
        {
            var jsonDiagnostic = JsonSerializer.SerializeToUtf8Bytes(diagnostic.JsonValue);
            await _eventSender.SendEventDataAsync(EventDataKind.DiagnosticEvent, jsonDiagnostic, 1);
            _testActionOnDiagnosticSend?.Invoke();
        }

        #endregion
    }
}
