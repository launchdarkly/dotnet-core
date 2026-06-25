using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal.Concurrent;

using static LaunchDarkly.Sdk.Internal.Events.EventTypes;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// The internal component that processes and delivers analytics events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This component is not visible to application code; the SDKs may choose to expose an
    /// interface for customizing event behavior, but if so, their default implementations of
    /// the interface will delegate to this component rather than this component implementing
    /// the interface itself. This allows us to make changes as needed to the internal interface
    /// and event parameters without disrupting application code, and also to provide internal
    /// features that may not be relevant to some SDKs (for instance, offline mode is only for
    /// use by Xamarin).
    /// </para>
    /// <para>
    /// The current implementation is really three components. EventProcessor is a simple
    /// facade that accepts event parameters (from SDK activity that might be happening on many
    /// threads) and pushes the events onto a queue. The queue is consumed by a single-threaded
    /// task run by EventProcessorInternal, which performs any necessary processing such as
    /// incrementing summary counters. When events are ready to deliver, it uses an
    /// implementation of IEventSender (normally DefaultEventSender) to deliver the JSON data.
    /// </para>
    /// </remarks>
    public sealed class EventProcessor : IDisposable
    {
        #region Private fields

        private readonly BlockingCollection<EventProcessorInternal.IEventMessage> _messageQueue;
        private readonly EventProcessorInternal _processorInternal;
        private readonly IDiagnosticStore _diagnosticStore;
        private readonly Timer _flushTimer;
        private readonly Timer _flushUsersTimer;
        private readonly TimeSpan _diagnosticRecordingInterval;
        private readonly Object _diagnosticTimerLock = new Object();
        private readonly Logger _logger;
        private Timer _diagnosticTimer;
        private AtomicBoolean _stopped;
        private AtomicBoolean _offline;
        private AtomicBoolean _sentInitialDiagnostics;
        private AtomicBoolean _inputCapacityExceeded;

        #endregion

        #region Constructor

        public EventProcessor(
            EventsConfiguration config,
            IEventSender eventSender,
            IContextDeduplicator contextDeduplicator,
            IDiagnosticStore diagnosticStore,
            IDiagnosticDisabler diagnosticDisabler,
            Logger logger,
            Action testActionOnDiagnosticSend
            )
        {
            _logger = logger;
            _stopped = new AtomicBoolean(false);
            _offline = new AtomicBoolean(false);
            _sentInitialDiagnostics = new AtomicBoolean(false);
            _inputCapacityExceeded = new AtomicBoolean(false);
            _messageQueue = new BlockingCollection<EventProcessorInternal.IEventMessage>(
                config.EventCapacity > 0 ? config.EventCapacity : 1);

            _processorInternal = new EventProcessorInternal(
                config,
                _messageQueue,
                eventSender,
                contextDeduplicator,
                diagnosticStore,
                _logger,
                testActionOnDiagnosticSend
                );

            if (config.EventFlushInterval > TimeSpan.Zero)
            {
                _flushTimer = new Timer(DoBackgroundFlush, null, config.EventFlushInterval,
                    config.EventFlushInterval);
            }
            _diagnosticStore = diagnosticStore;
            _diagnosticRecordingInterval = config.DiagnosticRecordingInterval;
            if (contextDeduplicator != null && contextDeduplicator.FlushInterval.HasValue)
            {
                _flushUsersTimer = new Timer(DoUserKeysFlush, null, contextDeduplicator.FlushInterval.Value,
                    contextDeduplicator.FlushInterval.Value);
            }
            else
            {
                _flushUsersTimer = null;
            }

            if (diagnosticStore != null)
            {
                SetupDiagnosticInit(diagnosticDisabler == null || !diagnosticDisabler.Disabled);

                if (diagnosticDisabler != null)
                {
                    diagnosticDisabler.DisabledChanged += ((sender, args) => SetupDiagnosticInit(!args.Disabled));
                }
            }
        }

        #endregion

        #region Public methods

        public void RecordEvaluationEvent(in EvaluationEvent e) =>
            SubmitMessage(new EventProcessorInternal.EventMessage(e));

        public void RecordIdentifyEvent(in IdentifyEvent e) =>
            SubmitMessage(new EventProcessorInternal.EventMessage(e));

        public void RecordCustomEvent(in CustomEvent e) =>
            SubmitMessage(new EventProcessorInternal.EventMessage(e));

        public void RecordMigrationOpEvent(in MigrationOpEvent e) =>
            SubmitMessage(new EventProcessorInternal.EventMessage(e));

        public void SetOffline(bool offline)
        {
            _offline.GetAndSet(offline);
            // Note that the offline state is known only to DefaultEventProcessor, not to EventDispatcher. We will
            // simply avoid sending any flush messages to EventDispatcher if we're offline. EventDispatcher will
            // never initiate a flush on its own.
        }

        /// <summary>
        /// Triggers an asynchronous event flush.
        /// </summary>
        public void Flush()
        {
            if (!_offline.Get())
            {
                SubmitMessage(new EventProcessorInternal.FlushMessage());
            }
        }

        /// <summary>
        /// Blocking version of <see cref="Flush"/>.
        /// </summary>
        /// <param name="timeout">maximum time to wait; zero or negative timeout means indefinitely</param>
        /// <returns>true if completed, false if timed out</returns>
        public bool FlushAndWait(TimeSpan timeout)
        {
            if (_offline.Get())
            {
                return false;
            }
            var message = new EventProcessorInternal.FlushMessage();
            SubmitMessage(message);
            return message.WaitForCompletion(timeout);
        }

        /// <summary>
        /// Asynchronous version of <see cref="FlushAndWait"/>.
        /// </summary>
        /// <remarks>
        /// The difference between this and <see cref="Flush"/> is that you can await the task to simulate
        /// blocking behavior.
        /// </remarks>
        /// <param name="timeout">maximum time to wait; zero or negative timeout means indefinitely</param>
        /// <returns>a task that resolves to true if completed, false if timed out</returns>
        public Task<bool> FlushAndWaitAsync(TimeSpan timeout)
        {
            if (_offline.Get())
            {
                return Task.FromResult(false);
            }
            var message = new EventProcessorInternal.FlushMessage();
            SubmitMessage(message);
            return message.WaitForCompletionAsync(timeout);
        }

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
                if (!_stopped.GetAndSet(true))
                {
                    _flushTimer?.Dispose();
                    _flushUsersTimer?.Dispose();

                    SubmitMessage(new EventProcessorInternal.FlushMessage());
                    var message = new EventProcessorInternal.ShutdownMessage();
                    SubmitMessage(message);
                    message.WaitForCompletion();

                    _processorInternal.Dispose();
                    _messageQueue.CompleteAdding();
                    _messageQueue.Dispose();
                }
            }
        }

        private bool SubmitMessage(EventProcessorInternal.IEventMessage message)
        {
            try
            {
                if (_messageQueue.TryAdd(message))
                {
                    _inputCapacityExceeded.GetAndSet(false);
                }
                else
                {
                    // This doesn't mean that the output event buffer is full, but rather that the main thread is
                    // seriously backed up with not-yet-processed events. We shouldn't see this.
                    if (!_inputCapacityExceeded.GetAndSet(true))
                    {
                        _logger.Warn("Events are being produced faster than they can be processed");
                    }
                    // If the message is a flush message, then it could never be completed if we cannot
                    // add it to the queue. So we are going to complete it here to prevent the calling
                    // code from hanging indefinitely.
                    switch (message)
                    {
                        case EventProcessorInternal.FlushMessage fm:
                            fm.Completed();
                            break;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // queue has been shut down
                return false;
            }
            return true;
        }

        private void SetupDiagnosticInit(bool enabled)
        {
            lock (_diagnosticTimerLock)
            {
                _diagnosticTimer?.Dispose();
                _diagnosticTimer = null;
                if (enabled)
                {
                    TimeSpan initialDelay = _diagnosticRecordingInterval - (DateTime.Now - _diagnosticStore.DataSince);
                    TimeSpan safeDelay =
                        (initialDelay < TimeSpan.Zero) ?
                        TimeSpan.Zero :
                        ((initialDelay > _diagnosticRecordingInterval) ? _diagnosticRecordingInterval : initialDelay);
                    _diagnosticTimer = new Timer(DoDiagnosticSend, null, safeDelay, _diagnosticRecordingInterval);
                }
            }
            // Send initial and persisted unsent event the first time diagnostics are started
            if (enabled && !_sentInitialDiagnostics.GetAndSet(true))
            {
                var unsent = _diagnosticStore.PersistedUnsentEvent;
                var init = _diagnosticStore.InitEvent;
                if (unsent.HasValue || init.HasValue)
                {
                    Task.Run(async () => // do these in a single task for test determinacy
                    {
                        if (unsent.HasValue)
                        {
                            await _processorInternal.SendDiagnosticEventAsync(unsent.Value);
                        }
                        if (init.HasValue)
                        {
                            await _processorInternal.SendDiagnosticEventAsync(init.Value);
                        }
                    });
                }
            }
        }

        // exposed for testing
        internal void WaitUntilInactive()
        {
            var message = new EventProcessorInternal.TestSyncMessage();
            SubmitMessage(message);
            message.WaitForCompletion();
        }

        private void DoBackgroundFlush(object stateInfo)
        {
            if (!_offline.Get())
            {
                SubmitMessage(new EventProcessorInternal.FlushMessage());
            }
        }

        private void DoUserKeysFlush(object stateInfo)
        {
            SubmitMessage(new EventProcessorInternal.FlushContextsMessage());
        }

        // exposed for testing 
        internal void DoDiagnosticSend(object stateInfo)
        {
            SubmitMessage(new EventProcessorInternal.DiagnosticMessage());
        }

        #endregion
    }
}
