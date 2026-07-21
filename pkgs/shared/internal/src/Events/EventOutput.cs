using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using static LaunchDarkly.Sdk.Internal.Events.EventTypes;
using static LaunchDarkly.Sdk.Json.LdJsonConverters;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal sealed class EventOutputFormatter
    {
        private readonly EventsConfiguration _config;
        private readonly EventContextFormatter _contextFormatter;

        public EventOutputFormatter(EventsConfiguration config)
        {
            _config = config;
            _contextFormatter = new EventContextFormatter(config);
        }

        public byte[] SerializeOutputEvents(object[] events, IReadOnlyList<EventSummary> summaries, out int eventCountOut)
        {
            using (var stream = new MemoryStream())
            {
                using (var jsonWriter = new Utf8JsonWriter(stream))
                {
                    eventCountOut = WriteOutputEvents(events, summaries, jsonWriter);
                    jsonWriter.Flush();
                    return stream.ToArray();
                }
            }
        }

        private struct MutableKeyValuePair<A, B>
        {
            public A Key { get; set; }
            public B Value { get; set; }

            public static MutableKeyValuePair<A, B> FromKeyValue(KeyValuePair<A, B> kv) =>
                new MutableKeyValuePair<A, B> {Key = kv.Key, Value = kv.Value};
        }

        public int WriteOutputEvents(object[] events, IReadOnlyList<EventSummary> summaries, Utf8JsonWriter w)
        {
            var eventCount = events.Length;
            w.WriteStartArray();
            foreach (var e in events)
            {
                WriteOutputEvent(e, w);
            }

            foreach (var summary in summaries)
            {
                if (!summary.Empty)
                {
                    WriteSummaryEvent(summary, w);
                    eventCount++;
                }
            }

            w.WriteEndArray();
            return eventCount;
        }

        public void WriteOutputEvent(object e, Utf8JsonWriter w)
        {
            w.WriteStartObject();
            switch (e)
            {
                case EvaluationEvent ee:
                    WriteEvaluationEvent(ee, w, false);
                    break;
                case IdentifyEvent ie:
                    WriteBase("identify", w, ie.Timestamp, null);
                    WriteContext(ie.Context, w);
                    break;
                case CustomEvent ce:
                    WriteBase("custom", w, ce.Timestamp, ce.EventKey);
                    WriteContext(ce.Context, w, redactAnonymous: _config.RedactAnonymousAllEvents);
                    JsonConverterHelpers.WriteLdValueIfNotNull(w, "data", ce.Data);
                    if (ce.MetricValue.HasValue)
                    {
                        w.WriteNumber("metricValue", ce.MetricValue.Value);
                    }

                    break;
                case MigrationOpEvent me:
                    WriteMigrationOpEvent(me, w);
                    break;
                case EventProcessorInternal.IndexEvent ie:
                    WriteBase("index", w, ie.Timestamp, null);
                    WriteContext(ie.Context, w);
                    break;
                case EventProcessorInternal.DebugEvent de:
                    WriteEvaluationEvent(de.FromEvent, w, true);
                    break;
                default:
                    break;
            }

            w.WriteEndObject();
        }

        private void WriteEvaluationEvent(in EvaluationEvent ee, Utf8JsonWriter obj, bool debug)
        {
            WriteBase(debug ? "debug" : "feature", obj, ee.Timestamp, ee.FlagKey);
            WriteContext(ee.Context, obj, redactAnonymous: !debug);

            if (ee.SamplingRatio.HasValue && ee.SamplingRatio != 1)
            {
                obj.WriteNumber("samplingRatio", ee.SamplingRatio.Value);
            }

            JsonConverterHelpers.WriteIntIfNotNull(obj, "version", ee.FlagVersion);
            JsonConverterHelpers.WriteIntIfNotNull(obj, "variation", ee.Variation);
            JsonConverterHelpers.WriteLdValue(obj, "value", ee.Value);
            JsonConverterHelpers.WriteLdValueIfNotNull(obj, "default", ee.Default);
            JsonConverterHelpers.WriteStringIfNotNull(obj, "prereqOf", ee.PrereqOf);
            WriteReason(ee.Reason, obj);
        }

        public void WriteSummaryEvent(EventSummary summary, Utf8JsonWriter w)
        {
            w.WriteStartObject();

            w.WriteString("kind", "summary");
            w.WriteNumber("startDate", summary.StartDate.Value);
            w.WriteNumber("endDate", summary.EndDate.Value);

            // Per-context summaries carry the evaluation context; aggregated summaries leave it
            // undefined and omit it. The context is filtered the same way as for other events.
            if (summary.Context.Defined)
            {
                WriteContext(summary.Context, w, redactAnonymous: true);
            }

            w.WriteStartObject("features");

            foreach (var kvFlag in summary.Flags)
            {
                w.WriteStartObject(kvFlag.Key);

                var flagSummary = kvFlag.Value;

                JsonConverterHelpers.WriteLdValue(w, "default", flagSummary.Default);

                w.WriteStartArray("contextKinds");
                foreach (var kind in flagSummary.ContextKinds)
                {
                    w.WriteStringValue(kind);
                }

                w.WriteEndArray();

                w.WriteStartArray("counters");

                foreach (var counter in flagSummary.Counters)
                {
                    w.WriteStartObject();
                    JsonConverterHelpers.WriteIntIfNotNull(w, "variation", counter.Key.Variation);
                    JsonConverterHelpers.WriteLdValue(w, "value", counter.Value.FlagValue);
                    JsonConverterHelpers.WriteIntIfNotNull(w, "version", counter.Key.Version);
                    JsonConverterHelpers.WriteBooleanIfTrue(w, "unknown", !counter.Key.Version.HasValue);
                    w.WriteNumber("count", counter.Value.Count);

                    w.WriteEndObject();
                }

                w.WriteEndArray();

                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        private void WriteBase(string kind, Utf8JsonWriter obj, UnixMillisecondTime creationDate, string key)
        {
            obj.WriteString("kind", kind);
            obj.WriteNumber("creationDate", creationDate.Value);
            JsonConverterHelpers.WriteStringIfNotNull(obj, "key", key);
        }

        private void WriteContext(in Context context, Utf8JsonWriter obj, bool redactAnonymous = false)
        {
            obj.WritePropertyName("context");
            _contextFormatter.Write(context, obj, redactAnonymous);
        }

        private static void WriteReason(EvaluationReason? reason, Utf8JsonWriter obj)
        {
            if (reason.HasValue)
            {
                obj.WritePropertyName("reason");
                EvaluationReasonConverter.WriteJsonValue(reason.Value, obj);
            }
        }

        private void WriteMigrationOpEvent(MigrationOpEvent migrationOpEvent, Utf8JsonWriter obj)
        {
            WriteBase("migration_op", obj, migrationOpEvent.Timestamp, null);
            WriteContext(migrationOpEvent.Context, obj, redactAnonymous: _config.RedactAnonymousAllEvents);
            if (migrationOpEvent.SamplingRatio != 1)
            {
                obj.WriteNumber("samplingRatio", migrationOpEvent.SamplingRatio);
            }

            obj.WriteString("operation", migrationOpEvent.Operation);
            WriteMigrationEvaluation(migrationOpEvent, obj);
            WriteMeasurements(migrationOpEvent, obj);
        }

        private static void WriteMigrationEvaluation(MigrationOpEvent migrationOpEvent, Utf8JsonWriter obj)
        {
            obj.WritePropertyName("evaluation");
            obj.WriteStartObject();
            obj.WriteString("key", migrationOpEvent.FlagKey);
            JsonConverterHelpers.WriteIntIfNotNull(obj, "version", migrationOpEvent.FlagVersion);
            JsonConverterHelpers.WriteIntIfNotNull(obj, "variation", migrationOpEvent.Variation);
            JsonConverterHelpers.WriteLdValue(obj, "value", migrationOpEvent.Value);
            JsonConverterHelpers.WriteLdValueIfNotNull(obj, "default", migrationOpEvent.Default);
            WriteReason(migrationOpEvent.Reason, obj);
            obj.WriteEndObject();
        }

        private static void WriteMeasurements(MigrationOpEvent migrationOpEvent, Utf8JsonWriter obj)
        {
            obj.WritePropertyName("measurements");
            obj.WriteStartArray();

            WriteInvokedMeasurement(migrationOpEvent, obj);
            WriteErrorMeasurement(migrationOpEvent, obj);
            WriteLatencyMeasurement(migrationOpEvent, obj);
            WriteConsistentMeasurement(migrationOpEvent, obj);

            obj.WriteEndArray();
        }

        private static void WriteInvokedMeasurement(MigrationOpEvent migrationOpEvent, Utf8JsonWriter obj)
        {
            obj.WriteStartObject();

            obj.WriteString("key", "invoked");
            obj.WritePropertyName("values");

            obj.WriteStartObject();
            if (migrationOpEvent.Invoked.Old)
            {
                obj.WriteBoolean("old", true);
            }

            if (migrationOpEvent.Invoked.New)
            {
                obj.WriteBoolean("new", true);
            }

            obj.WriteEndObject(); // end values

            obj.WriteEndObject(); // end measurement
        }

        private static void WriteLatencyMeasurement(MigrationOpEvent migrationOpEvent, Utf8JsonWriter obj)
        {
            if (!migrationOpEvent.Latency.HasValue ||
                (!migrationOpEvent.Latency.Value.Old.HasValue && !migrationOpEvent.Latency.Value.New.HasValue)) return;

            obj.WriteStartObject();

            obj.WriteString("key", "latency_ms");
            obj.WritePropertyName("values");

            obj.WriteStartObject();
            if (migrationOpEvent.Latency.Value.Old.HasValue)
            {
                obj.WriteNumber("old", migrationOpEvent.Latency.Value.Old.Value);
            }

            if (migrationOpEvent.Latency.Value.New.HasValue)
            {
                obj.WriteNumber("new", migrationOpEvent.Latency.Value.New.Value);
            }

            obj.WriteEndObject(); // end values

            obj.WriteEndObject(); // end measurement
        }

        private static void WriteErrorMeasurement(MigrationOpEvent migrationOpEvent, Utf8JsonWriter obj)
        {
            if (!migrationOpEvent.Error.HasValue ||
                (!migrationOpEvent.Error.Value.Old && !migrationOpEvent.Error.Value.New)) return;

            obj.WriteStartObject();
            obj.WriteString("key", "error");
            obj.WritePropertyName("values");

            obj.WriteStartObject();
            if (migrationOpEvent.Error.Value.Old)
            {
                obj.WriteBoolean("old", true);
            }

            if (migrationOpEvent.Error.Value.New)
            {
                obj.WriteBoolean("new", true);
            }

            obj.WriteEndObject(); // end values

            obj.WriteEndObject(); // end measurement
        }

        private static void WriteConsistentMeasurement(MigrationOpEvent migrationOpEvent, Utf8JsonWriter obj)
        {
            if (!migrationOpEvent.Consistent.HasValue) return;

            obj.WriteStartObject();
            obj.WriteString("key", "consistent");
            obj.WriteBoolean("value", migrationOpEvent.Consistent.Value.IsConsistent);
            if (migrationOpEvent.Consistent.Value.SamplingRatio != 1)
            {
                obj.WriteNumber("samplingRatio", migrationOpEvent.Consistent.Value.SamplingRatio);
            }

            obj.WriteEndObject();
        }
    }
}
