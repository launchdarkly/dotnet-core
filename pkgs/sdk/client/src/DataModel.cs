using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Json;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Client
{
    /// <summary>
    /// Contains information about the internal data model for feature flag state.
    /// </summary>
    /// <remarks>
    /// The details of the data model are not public to application code (although of course developers can easily
    /// look at the code or the data) so that changes to LaunchDarkly SDK implementation details will not be breaking
    /// changes to the application. Therefore, most of the members of this class are internal. The public members
    /// provide a high-level description of model objects so that custom integration code or test code can store or
    /// serialize them.
    /// </remarks>
    public static class DataModel
    {
        /// <summary>
        /// Represents the state of a feature flag evaluation received from LaunchDarkly.
        /// </summary>
        [JsonConverter(typeof(FeatureFlagJsonConverter))]
        public sealed class FeatureFlag : IEquatable<FeatureFlag>, IJsonSerializable
        {
            internal LdValue Value { get; }
            internal int? Variation { get; }
            internal EvaluationReason? Reason { get; }
            internal int Version { get; }
            internal int? FlagVersion { get; }
            internal bool TrackEvents { get; }
            internal bool TrackReason { get; }
            internal UnixMillisecondTime? DebugEventsUntilDate { get; }

            internal FeatureFlag(
                LdValue value,
                int? variation,
                EvaluationReason? reason,
                int version,
                int? flagVersion,
                bool trackEvents,
                bool trackReason,
                UnixMillisecondTime? debugEventsUntilDate
                )
            {
                Value = value;
                Variation = variation;
                Reason = reason;
                Version = version;
                FlagVersion = flagVersion;
                TrackEvents = trackEvents;
                TrackReason = trackReason;
                DebugEventsUntilDate = debugEventsUntilDate;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) =>
                Equals(obj as FeatureFlag);

            /// <inheritdoc/>
            public bool Equals(FeatureFlag otherFlag) =>
                Value.Equals(otherFlag.Value)
                && Variation == otherFlag.Variation
                && Reason.Equals(otherFlag.Reason)
                && Version == otherFlag.Version
                && FlagVersion == otherFlag.FlagVersion
                && TrackEvents == otherFlag.TrackEvents
                && DebugEventsUntilDate == otherFlag.DebugEventsUntilDate;

            /// <inheritdoc/>
            public override int GetHashCode() =>
                Value.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() =>
                string.Format("({0},{1},{2},{3},{4},{5},{6},{7})",
                    Value, Variation, Reason, Version, FlagVersion, TrackEvents, TrackReason, DebugEventsUntilDate);

            internal ItemDescriptor ToItemDescriptor() =>
                new ItemDescriptor(Version, this);
        }

        internal sealed class FeatureFlagJsonConverter : JsonConverter<FeatureFlag>
        {
            public override FeatureFlag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                ReadJsonValue(ref reader);

            public static FeatureFlag ReadJsonValue(ref Utf8JsonReader reader)
            {
                LdValue value = LdValue.Null;
                int version = 0;
                int? flagVersion = null;
                int? variation = null;
                EvaluationReason? reason = null;
                bool trackEvents = false;
                bool trackReason = false;
                UnixMillisecondTime? debugEventsUntilDate = null;

                for (var obj = RequireObject(ref reader); obj.Next(ref reader);)
                {
                    switch (obj.Name)
                    {
                        case "value":
                            value = LdJsonConverters.LdValueConverter.ReadJsonValue(ref reader);
                            break;
                        case "version":
                            version = reader.GetInt32();
                            break;
                        case "flagVersion":
                            flagVersion = JsonConverterHelpers.GetIntOrNull(ref reader);
                            break;
                        case "variation":
                            variation = JsonConverterHelpers.GetIntOrNull(ref reader);
                            break;
                        case "reason":
                            reason = JsonSerializer.Deserialize<EvaluationReason?>(ref reader);
                            break;
                        case "trackEvents":
                            trackEvents = reader.GetBoolean();
                            break;
                        case "trackReason":
                            trackReason = reader.GetBoolean();
                            break;
                        case "debugEventsUntilDate":
                            debugEventsUntilDate = JsonSerializer.Deserialize<UnixMillisecondTime?>(ref reader);
                            break;
                    }
                }

                return new FeatureFlag(
                    value,
                    variation,
                    reason,
                    version,
                    flagVersion,
                    trackEvents,
                    trackReason,
                    debugEventsUntilDate
                    );
            }

            public override void Write(Utf8JsonWriter writer, FeatureFlag value, JsonSerializerOptions options) =>
                WriteJsonValue(value, writer);

            public static void WriteJsonValue(FeatureFlag value, Utf8JsonWriter writer)
            {
                writer.WriteStartObject();

                JsonConverterHelpers.WriteLdValue(writer, "value", value.Value);
                writer.WriteNumber("version", value.Version);
                JsonConverterHelpers.WriteIntIfNotNull(writer, "flagVersion", value.FlagVersion);
                JsonConverterHelpers.WriteIntIfNotNull(writer, "variation", value.Variation);
                if (value.Reason.HasValue)
                {
                    writer.WritePropertyName("reason");
                    JsonSerializer.Serialize(writer, value.Reason.Value);
                }
                JsonConverterHelpers.WriteBooleanIfTrue(writer, "trackEvents", value.TrackEvents);
                JsonConverterHelpers.WriteBooleanIfTrue(writer, "trackReason", value.TrackReason);
                if (value.DebugEventsUntilDate.HasValue)
                {
                    writer.WriteNumber("debugEventsUntilDate", value.DebugEventsUntilDate.Value.Value);
                }

                writer.WriteEndObject();
            }
        }
    }
}
