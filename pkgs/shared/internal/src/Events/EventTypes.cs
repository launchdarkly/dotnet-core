namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// This class contains inner types that are used as parameter types for the EventProcessor
    /// methods for recording different kinds of events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason we define these as types, rather than just having those methods take a bunch
    /// of individual parameters like timestamp and key, is to make it as easy as possible to add
    /// new optional properties: adding a parameter to a method is a backward-incompatible change
    /// (unless we add overloads, in which case we'll keep accumulating overloads) whereas adding
    /// a property to a struct means it'll just have its default value if the caller doesn't set
    /// it. We could get a similar effect by using named method parameters with defaults, but
    /// that only ensures compile-time compatibility - at runtime the method still has a
    /// different signature - so this way is a bit cleaner.
    /// </para>
    /// <para>
    /// Note that these are declared as structs rather than classes so that, at least when
    /// they're originally created, they can live on the stack instead of the heap. They will
    /// still eventually end up getting treated as class instances and moved to the heap, because
    /// of .NET's boxing rules: whenever a struct is treated as a dynamically-typed object (i.e.
    /// when we wrap it in an EventMessage to put it on our queue), it is boxed. But until that
    /// point, application code can freely create these and pass them around without incurring
    /// any allocations, right up until the moment when EventProcessor decides to put the event
    /// onto a queue (if it does). This is also why there's a separate EventProcessor method
    /// for each type, rather than having a single RecordEvent that takes a base type: using
    /// polymorphism in that way would cause boxing to always happen.
    /// </para>
    /// </remarks>
    public static class EventTypes
    {
        /// <summary>
        /// Parameters for <see cref="EventProcessor.RecordEvaluationEvent(EvaluationEvent)"/>.
        /// Note that the "kind" string identifying this type of event in JSON data is "feature",
        /// not "evaluation".
        /// </summary>
        public struct EvaluationEvent
        {
            public UnixMillisecondTime Timestamp;
            public Context Context;
            public string FlagKey;
            public int? FlagVersion;
            public int? Variation;
            public LdValue Value;
            public LdValue Default;
            public EvaluationReason? Reason;
            public string PrereqOf;
            public bool TrackEvents;
            public UnixMillisecondTime? DebugEventsUntilDate;
            // This is optional so that the default value can be disambiguated from 0.
            // Allowing libraries to update to the version where this was introduced without inadvertently
            // disabling events.
            public long? SamplingRatio;
            public bool ExcludeFromSummaries;
        }

        /// <summary>
        /// Parameters for <see cref="EventProcessor.RecordIdentifyEvent(IdentifyEvent)"/>.
        /// </summary>
        public struct IdentifyEvent
        {
            public UnixMillisecondTime Timestamp;
            public Context Context;
        }

        /// <summary>
        /// Parameters for <see cref="EventProcessor.RecordCustomEvent(UnixMillisecondTime, User, string, LdValue, double?)"/>.
        /// </summary>
        public struct CustomEvent
        {
            public UnixMillisecondTime Timestamp;
            public Context Context;
            public string EventKey;
            public LdValue Data;
            public double? MetricValue;
        }

        /// <summary>
        /// Parameters for <see cref="EventProcessor.RecordMigrationOpEvent"/>
        /// </summary>
        public struct MigrationOpEvent
        {
            #region Measurement Types

            public struct InvokedMeasurement
            {
                public bool Old;
                public bool New;
            }

            public struct LatencyMeasurement
            {
                public long? Old;
                public long? New;
            }

            public struct ErrorMeasurement
            {
                public bool Old;
                public bool New;
            }

            public struct ConsistentMeasurement
            {
                public bool IsConsistent;
                public long SamplingRatio;
            }

            #endregion

            public UnixMillisecondTime Timestamp;
            public Context Context;
            public string Operation;
            public long SamplingRatio;

            #region Evaluation Detail

            public string FlagKey;
            public int? FlagVersion;
            public int? Variation;
            public LdValue Value;
            public LdValue Default;
            public EvaluationReason? Reason;

            #endregion

            #region Measurements

            public InvokedMeasurement Invoked;
            public LatencyMeasurement? Latency;
            public ErrorMeasurement? Error;
            public ConsistentMeasurement? Consistent;

            #endregion
        }
    }
}
