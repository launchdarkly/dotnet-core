﻿
namespace LaunchDarkly.Sdk.Client.Subsystems
{
    /// <summary>
    /// Parameter types for use by <see cref="IEventProcessor"/> implementations.
    /// </summary>
    /// <remarks>
    /// Application code normally does not need to use these types or interact directly with any
    /// <see cref="IEventProcessor"/> functionality. They are provided to allow a custom implementation
    /// or test fixture to be substituted for the SDK's normal analytics event logic.
    /// </remarks>
    public static class EventProcessorTypes
    {
        /// <summary>
        /// Parameters for <see cref="IEventProcessor.RecordEvaluationEvent(in EvaluationEvent)"/>.
        /// </summary>
        public struct EvaluationEvent
        {
            /// <summary>
            /// Date/timestamp of the event.
            /// </summary>
            public UnixMillisecondTime Timestamp { get; set; }

            /// <summary>
            /// The context for the evaluation. Some attributes may not be sent  to LaunchDarkly if they
            /// are private.
            /// </summary>
            public Context Context { get; set; }

            /// <summary>
            /// The unique key of the feature flag involved in the event.
            /// </summary>
            public string FlagKey { get; set; }

            /// <summary>
            /// The version of the flag.
            /// </summary>
            public int? FlagVersion { get; set; }

            /// <summary>
            /// The variation index for the computed value of the flag.
            /// </summary>
            public int? Variation { get; set; }

            /// <summary>
            /// The computed value of the flag.
            /// </summary>
            public LdValue Value { get; set; }

            /// <summary>
            /// The default value of the flag.
            /// </summary>
            public LdValue Default { get; set; }

            /// <summary>
            /// An explanation of how the value was calculated, or null if the reason was not requested.
            /// </summary>
            public EvaluationReason? Reason { get; set; }

            /// <summary>
            /// The key of the flag that this flag is a prerequisite of, if any.
            /// </summary>
            public string PrerequisiteOf { get; set; }

            /// <summary>
            /// True if full-fidelity analytics events should be sent for this flag.
            /// </summary>
            public bool TrackEvents { get; set; }

            /// <summary>
            /// If set, debug events are being generated until this date/time.
            /// </summary>
            public UnixMillisecondTime? DebugEventsUntilDate { get; set; }
        }

        /// <summary>
        /// Parameters for <see cref="IEventProcessor.RecordIdentifyEvent(in IdentifyEvent)"/>.
        /// </summary>
        public struct IdentifyEvent
        {
            /// <summary>
            /// Date/timestamp of the event.
            /// </summary>
            public UnixMillisecondTime Timestamp { get; set; }

            /// <summary>
            /// The evaluation context associated with the event. Some attributes may not be sent
            /// to LaunchDarkly if they are private.
            /// </summary>
            public Context Context { get; set; }
        }

        /// <summary>
        /// Parameters for <see cref="IEventProcessor.RecordCustomEvent(in CustomEvent)"/>.
        /// </summary>
        public struct CustomEvent
        {
            /// <summary>
            /// Date/timestamp of the event.
            /// </summary>
            public UnixMillisecondTime Timestamp { get; set; }
            /// <summary>
            /// The evaluation context associated with the event. Some attributes may not be sent
            /// to LaunchDarkly if they are private.
            /// </summary>
            public Context Context { get; set; }

            /// <summary>
            /// The event key.
            /// </summary>
            public string EventKey { get; set; }


            /// <summary>
            /// Custom data provided for the event.
            /// </summary>
            public LdValue Data { get; set; }

            /// <summary>
            /// An optional numeric value that can be used in analytics.
            /// </summary>
            public double? MetricValue { get; set; }
        }
    }
}
