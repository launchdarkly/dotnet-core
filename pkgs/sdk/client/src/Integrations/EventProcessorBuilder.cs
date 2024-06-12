using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Client.Internal.Events;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Events;

using static LaunchDarkly.Sdk.Internal.Events.DiagnosticConfigProperties;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    /// <summary>
    /// Contains methods for configuring delivery of analytics events.
    /// </summary>
    /// <remarks>
    /// The SDK normally buffers analytics events and sends them to LaunchDarkly at intervals. If you want
    /// to customize this behavior, create a builder with <see cref="Components.SendEvents"/>, change its
    /// properties with the methods of this class, and pass it to
    /// <see cref="ConfigurationBuilder.Events(IComponentConfigurer{IEventProcessor})"/>.
    /// </remarks>
    /// <example>
    /// <code>
    ///     var config = Configuration.Builder(sdkKey)
    ///         .Events(
    ///             Components.SendEvents().Capacity(5000).FlushInterval(TimeSpan.FromSeconds(2))
    ///         )
    ///         .Build();
    /// </code>
    /// </example>
    public sealed class EventProcessorBuilder : IComponentConfigurer<IEventProcessor>, IDiagnosticDescription
    {
        /// <summary>
        /// The default value for <see cref="Capacity(int)"/>.
        /// </summary>
        public const int DefaultCapacity = 100;

        /// <summary>
        /// The default value for <see cref="DiagnosticRecordingInterval(TimeSpan)"/>.
        /// </summary>
        public static readonly TimeSpan DefaultDiagnosticRecordingInterval = TimeSpan.FromMinutes(15);

        /// <summary>
        /// The default value for <see cref="FlushInterval(TimeSpan)"/>.
        /// </summary>
        /// <remarks>
        /// For Android and iOS, this is 30 seconds. For all other platforms, it is 5 seconds. The
        /// difference is because the extra HTTP requests for sending events more frequently are
        /// undesirable in a mobile application as opposed to a desktop application.
        /// </remarks>
        public static readonly TimeSpan DefaultFlushInterval =
#if (ANDROID || IOS)
            TimeSpan.FromSeconds(30);
#else
            TimeSpan.FromSeconds(5);
#endif

        /// <summary>
        /// The minimum value for <see cref="DiagnosticRecordingInterval(TimeSpan)"/>: 5 minutes.
        /// </summary>
        public static readonly TimeSpan MinimumDiagnosticRecordingInterval = TimeSpan.FromMinutes(5);

        internal bool _allAttributesPrivate = false;
        internal int _capacity = DefaultCapacity;
        internal TimeSpan _diagnosticRecordingInterval = DefaultDiagnosticRecordingInterval;
        internal TimeSpan _flushInterval = DefaultFlushInterval;
        internal HashSet<AttributeRef> _privateAttributes = new HashSet<AttributeRef>();
        internal IEventSender _eventSender = null; // used in testing

        /// <summary>
        /// Sets whether or not all optional user attributes should be hidden from LaunchDarkly.
        /// </summary>
        /// <remarks>
        /// If this is <see langword="true"/>, all user attribute values (other than the key) will be private, not just
        /// the attributes specified in <see cref="PrivateAttributes(string[])"/> or on a per-user basis with
        /// <see cref="UserBuilder"/> methods. By default, it is <see langword="false"/>.
        /// </remarks>
        /// <param name="allAttributesPrivate">true if all user attributes should be private</param>
        /// <returns>the builder</returns>
        public EventProcessorBuilder AllAttributesPrivate(bool allAttributesPrivate)
        {
            _allAttributesPrivate = allAttributesPrivate;
            return this;
        }

        /// <summary>
        /// Sets the capacity of the events buffer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The client buffers up to this many events in memory before flushing. If the capacity is exceeded before
        /// the buffer is flushed (see <see cref="FlushInterval(TimeSpan)"/>), events will be discarded. Increasing the
        /// capacity means that events are less likely to be discarded, at the cost of consuming more memory.
        /// </para>
        /// <para>
        /// The default value is <see cref="DefaultCapacity"/>. A zero or negative value will be changed to the default.
        /// </para>
        /// </remarks>
        /// <param name="capacity">the capacity of the event buffer</param>
        /// <returns>the builder</returns>
        public EventProcessorBuilder Capacity(int capacity)
        {
            _capacity = (capacity <= 0) ? DefaultCapacity : capacity;
            return this;
        }

        /// <summary>
        /// Sets the interval at which periodic diagnostic data is sent.
        /// </summary>
        /// <remarks>
        /// The default value is <see cref="DefaultDiagnosticRecordingInterval"/>; the minimum value is
        /// <see cref="MinimumDiagnosticRecordingInterval"/>. This property is ignored if
        /// <see cref="ConfigurationBuilder.DiagnosticOptOut(bool)"/> is set to <see langword="true"/>.
        /// </remarks>
        /// <param name="diagnosticRecordingInterval">the diagnostics interval</param>
        /// <returns>the builder</returns>
        public EventProcessorBuilder DiagnosticRecordingInterval(TimeSpan diagnosticRecordingInterval)
        {
            _diagnosticRecordingInterval =
                diagnosticRecordingInterval < MinimumDiagnosticRecordingInterval ?
                MinimumDiagnosticRecordingInterval : diagnosticRecordingInterval;
            return this;
        }

        // Used only in testing
        internal EventProcessorBuilder DiagnosticRecordingIntervalNoMinimum(TimeSpan diagnosticRecordingInterval)
        {
            _diagnosticRecordingInterval = diagnosticRecordingInterval;
            return this;
        }

        // Used only in testing
        internal EventProcessorBuilder EventSender(IEventSender eventSender)
        {
            _eventSender = eventSender;
            return this;
        }

        /// <summary>
        /// Sets the interval between flushes of the event buffer.
        /// </summary>
        /// <remarks>
        /// Decreasing the flush interval means that the event buffer is less likely to reach capacity.
        /// The default value is <see cref="DefaultFlushInterval"/>. A zero or negative value will be changed to
        /// the default.
        /// </remarks>
        /// <param name="flushInterval">the flush interval</param>
        /// <returns>the builder</returns>
        public EventProcessorBuilder FlushInterval(TimeSpan flushInterval)
        {
            _flushInterval = (flushInterval.CompareTo(TimeSpan.Zero) <= 0) ?
                DefaultFlushInterval : flushInterval;
            return this;
        }

        internal EventProcessorBuilder FlushIntervalNoMinimum(TimeSpan flushInterval)
        {
            _flushInterval = flushInterval;
            return this;
        }

        /// <summary>
        /// Marks a set of attribute names as private.
        /// </summary>
        /// <remarks>
        /// Any contexts sent to LaunchDarkly with this configuration active will have attributes with these
        /// names removed. This is in addition to any attributes that were marked as private for an
        /// individual context with <see cref="ContextBuilder"/> methods.
        /// </remarks>
        /// <param name="attributes">a set of attributes that will be removed from context data set to LaunchDarkly</param>
        /// <returns>the builder</returns>
        public EventProcessorBuilder PrivateAttributes(params string[] attributes)
        {
            foreach (var a in attributes)
            {
                _privateAttributes.Add(AttributeRef.FromPath(a));
            }
            return this;
        }

        /// <inheritdoc/>
        public IEventProcessor Build(LdClientContext context)
        {
            var eventsConfig = MakeEventsConfiguration(context, true);
            var logger = context.BaseLogger.SubLogger(LogNames.EventsSubLog);
            var eventSender = _eventSender ??
                new DefaultEventSender(
                    context.Http.HttpProperties,
                    eventsConfig,
                    logger
                    );
            return new DefaultEventProcessorWrapper(
                new EventProcessor(
                    eventsConfig,
                    eventSender,
                    null, // no user deduplicator, because the client-side SDK doesn't send index events
                    context.DiagnosticStore,
                    context.DiagnosticDisabler,
                    logger,
                    null
                    ));
        }

        /// <inheritdoc/>
        public LdValue DescribeConfiguration(LdClientContext context) =>
            LdValue.BuildObject().WithEventProperties(
                MakeEventsConfiguration(context, false),
                StandardEndpoints.IsCustomUri(context.ServiceEndpoints, e => e.EventsBaseUri)
                )
                .Build();

        private EventsConfiguration MakeEventsConfiguration(LdClientContext context, bool logConfigErrors)
        {
            var baseUri = StandardEndpoints.SelectBaseUri(
                context.ServiceEndpoints,
                e => e.EventsBaseUri,
                "Events",
                logConfigErrors ? context.BaseLogger : Logs.None.Logger("")
                );
            return new EventsConfiguration
            {
                AllAttributesPrivate = _allAttributesPrivate,
                EventCapacity = _capacity,
                EventFlushInterval = _flushInterval,
                EventsUri = baseUri.AddPath(StandardEndpoints.AnalyticsEventsPostRequestPath),
                DiagnosticRecordingInterval = _diagnosticRecordingInterval,
                DiagnosticUri = baseUri.AddPath(StandardEndpoints.DiagnosticEventsPostRequestPath),
                PrivateAttributes = _privateAttributes.ToImmutableHashSet(),
                RetryInterval = TimeSpan.FromSeconds(1)
            };
        }
    }
}
