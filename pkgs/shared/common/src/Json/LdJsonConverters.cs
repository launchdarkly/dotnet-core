using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace LaunchDarkly.Sdk.Json
{
    /// <summary>
    /// Low-level JSON custom serializations for SDK types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applications normally will not need to reference these types; they are used automatically
    /// when you call <see cref="LdJsonSerialization"/> methods (or <c>System.Text.Json</c>
    /// methods, if that API is available). They are included here for use by other LaunchDarkly
    /// library code.
    /// </para>
    /// <para>
    /// Some of these converters also have <c>ReadJsonValue</c> and <c>WriteJsonValue</c> methods.
    /// The reason for this is that the <c>object</c> type used by the regular converter methods
    /// causes boxing/unboxing conversions if the target type is a <c>struct</c>, and if the
    /// overhead of these is a concern it is more efficient to call a strongly typed method.
    /// </para>
    /// </remarks>
    /// <seealso cref="LdJsonSerialization"/>
    public static partial class LdJsonConverters
    {
        private static void RequireToken(ref Utf8JsonReader reader, JsonTokenType expectedType,
            string propName = null)
        {
            if (reader.TokenType != expectedType)
            {
                throw new JsonException("Expected " + expectedType + ", got " + reader.TokenType +
                    (propName is null ? "" : (" for property \"" + propName + "\"")));
            }
        }

        private static bool ConsumeNull(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                reader.Read();
                return true;
            }
            return false;
        }

        private static bool NextProperty(ref Utf8JsonReader reader, out string name)
        {
            if (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                name = reader.GetString();
                reader.Read();
                return true;
            }
            name = null;
            return false;
        }

#pragma warning disable CS1591 // don't bother with XML comments for these low-level helpers
        public sealed class EvaluationReasonConverter : JsonConverter<EvaluationReason>
        {
            public override void Write(Utf8JsonWriter writer, EvaluationReason value, JsonSerializerOptions options) =>
                WriteJsonValue(value, writer);

            public override EvaluationReason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                ReadJsonValue(ref reader);

            public static EvaluationReason ReadJsonValue(ref Utf8JsonReader reader)
            {
                EvaluationReasonKind? kind = null;
                int? ruleIndex = null;
                string ruleId = null;
                string prerequisiteKey = null;
                EvaluationErrorKind? errorKind = null;
                bool inExperiment = false;
                BigSegmentsStatus? bigSegmentsStatus = null;

                RequireToken(ref reader, JsonTokenType.StartObject);
                while (NextProperty(ref reader, out var name))
                {
                    switch (name)
                    {
                        case "kind":
                            kind = EvaluationReasonKindConverter.FromIdentifier(reader.GetString());
                            break;
                        case "ruleIndex":
                            ruleIndex = reader.GetInt32();
                            break;
                        case "ruleId":
                            ruleId = reader.GetString();
                            break;
                        case "prerequisiteKey":
                            prerequisiteKey = reader.GetString();
                            break;
                        case "errorKind":
                            errorKind = EvaluationErrorKindConverter.FromIdentifier(reader.GetString());
                            break;
                        case "inExperiment":
                            inExperiment = reader.GetBoolean();
                            break;
                        case "bigSegmentsStatus":
                            bigSegmentsStatus = BigSegmentsStatusConverter.FromIdentifier(reader.GetString());
                            break;
                        default:
                            reader.Skip();
                            break;
                        }
                    }

                    if (!kind.HasValue)
                    {
                        throw new JsonException("Missing required property: kind");
                    }

                    EvaluationReason reason;
                    switch (kind.Value)
                    {
                        case EvaluationReasonKind.Off:
                            reason = EvaluationReason.OffReason;
                            break;
                        case EvaluationReasonKind.Fallthrough:
                            reason = EvaluationReason.FallthroughReason;
                            break;
                        case EvaluationReasonKind.TargetMatch:
                            reason = EvaluationReason.TargetMatchReason;
                            break;
                        case EvaluationReasonKind.RuleMatch:
                            reason = EvaluationReason.RuleMatchReason(ruleIndex ?? 0, ruleId);
                            break;
                        case EvaluationReasonKind.PrerequisiteFailed:
                            reason = EvaluationReason.PrerequisiteFailedReason(prerequisiteKey);
                            break;
                        case EvaluationReasonKind.Error:
                            reason = EvaluationReason.ErrorReason(errorKind ?? EvaluationErrorKind.Exception);
                            break;
                        default: // shouldn't be possible, all of the enum values are accounted for
                            reason = new EvaluationReason();
                            break;
                    }
                    if (inExperiment)
                    {
                        reason = reason.WithInExperiment(true);
                    }
                    if (bigSegmentsStatus.HasValue)
                    {
                        reason = reason.WithBigSegmentsStatus(bigSegmentsStatus);
                    }
                    return reason;
            }

            public static void WriteJsonValue(EvaluationReason value, Utf8JsonWriter writer)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", EvaluationReasonKindConverter.ToIdentifier(value.Kind));
                switch (value.Kind)
                {
                    case EvaluationReasonKind.RuleMatch:
                        writer.WriteNumber("ruleIndex", value.RuleIndex ?? 0);
                        writer.WriteString("ruleId", value.RuleId);
                        break;
                    case EvaluationReasonKind.PrerequisiteFailed:
                        writer.WriteString("prerequisiteKey", value.PrerequisiteKey);
                        break;
                    case EvaluationReasonKind.Error:
                        writer.WritePropertyName("errorKind");
                        EvaluationErrorKindConverter.WriteJsonValue(value.ErrorKind.Value, writer);
                        break;
                }
                if (value.InExperiment)
                {
                    writer.WriteBoolean("inExperiment", true); // omit property if false
                }
                if (value.BigSegmentsStatus.HasValue)
                {
                    writer.WritePropertyName("bigSegmentsStatus");
                    BigSegmentsStatusConverter.WriteJsonValue(value.BigSegmentsStatus.Value, writer);
                }
                writer.WriteEndObject();
            }
        }

        public sealed class BigSegmentsStatusConverter : JsonConverter<BigSegmentsStatus>
        {
            public override void Write(Utf8JsonWriter writer, BigSegmentsStatus value, JsonSerializerOptions options) =>
                WriteJsonValue(value, writer);

            public override BigSegmentsStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                ReadJsonValue(ref reader);

            public static BigSegmentsStatus ReadJsonValue(ref Utf8JsonReader reader) =>
                FromIdentifier(reader.GetString());

            public static void WriteJsonValue(BigSegmentsStatus instance, Utf8JsonWriter writer) =>
                writer.WriteStringValue(ToIdentifier(instance));

            internal static BigSegmentsStatus FromIdentifier(string value)
            {
                foreach (BigSegmentsStatus k in
#if NET5_0_OR_GREATER
                    Enum.GetValues<BigSegmentsStatus>()
#else
                    Enum.GetValues(typeof(BigSegmentsStatus))
#endif
                )
                {
                    if (ToIdentifier(k) == value)
                    {
                        return k;
                    }
                }
                throw new ArgumentException("invalid BigSegmentsStatus");
            }

            internal static string ToIdentifier(BigSegmentsStatus value)
            {
                switch (value)
                {
                    case BigSegmentsStatus.Healthy:
                        return "HEALTHY";
                    case BigSegmentsStatus.Stale:
                        return "STALE";
                    case BigSegmentsStatus.NotConfigured:
                        return "NOT_CONFIGURED";
                    case BigSegmentsStatus.StoreError:
                        return "STORE_ERROR";
                    default:
                        throw new ArgumentException();
                }
            }
        }

        /// <summary>
        /// The JSON converter for <see cref="EvaluationErrorKind"/>.
        /// </summary>
        public sealed class EvaluationErrorKindConverter : JsonConverter<EvaluationErrorKind>
        {
            public override void Write(Utf8JsonWriter writer, EvaluationErrorKind value, JsonSerializerOptions options) =>
                WriteJsonValue(value, writer);

            public override EvaluationErrorKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                ReadJsonValue(ref reader);

            public static EvaluationErrorKind ReadJsonValue(ref Utf8JsonReader reader) =>
                FromIdentifier(reader.GetString());

            public static void WriteJsonValue(EvaluationErrorKind instance, Utf8JsonWriter writer) =>
                writer.WriteStringValue(ToIdentifier(instance));

            internal static EvaluationErrorKind FromIdentifier(string value)
            {
                foreach (EvaluationErrorKind k in
#if NET5_0_OR_GREATER
                    Enum.GetValues<EvaluationErrorKind>()
#else
                    Enum.GetValues(typeof(EvaluationErrorKind))
#endif
                )
                {
                    if (ToIdentifier(k) == value)
                    {
                        return k;
                    }
                }
                throw new ArgumentException("invalid EvaluationErrorKind");
            }

            internal static string ToIdentifier(EvaluationErrorKind value)
            {
                switch (value)
                {
                    case EvaluationErrorKind.ClientNotReady:
                        return "CLIENT_NOT_READY";
                    case EvaluationErrorKind.FlagNotFound:
                        return "FLAG_NOT_FOUND";
                    case EvaluationErrorKind.UserNotSpecified:
                        return "USER_NOT_SPECIFIED";
                    case EvaluationErrorKind.MalformedFlag:
                        return "MALFORMED_FLAG";
                    case EvaluationErrorKind.WrongType:
                        return "WRONG_TYPE";
                    case EvaluationErrorKind.Exception:
                        return "EXCEPTION";
                    default:
                        throw new ArgumentException();
                }
            }
        }

        /// <summary>
        /// The JSON converter for <see cref="EvaluationReasonKind"/>.
        /// </summary>
        public sealed class EvaluationReasonKindConverter : JsonConverter<EvaluationReasonKind>
        {
            public override void Write(Utf8JsonWriter writer, EvaluationReasonKind value, JsonSerializerOptions options) =>
                WriteJsonValue(value, writer);

            public override EvaluationReasonKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                ReadJsonValue(ref reader);

            public EvaluationReasonKind ReadJsonValue(ref Utf8JsonReader reader) =>
                FromIdentifier(reader.GetString());

            public void WriteJsonValue(EvaluationReasonKind instance, Utf8JsonWriter writer) =>
                writer.WriteStringValue(ToIdentifier(instance));

            internal static EvaluationReasonKind FromIdentifier(string value)
            {
                foreach (EvaluationReasonKind k in
#if NET5_0_OR_GREATER
                    Enum.GetValues<EvaluationReasonKind>()
#else
                    Enum.GetValues(typeof(EvaluationReasonKind))
#endif
                )
                {
                    if (ToIdentifier(k) == value)
                    {
                        return k;
                    }
                }
                throw new ArgumentException("invalid EvaluationReasonKind");
            }

            internal static string ToIdentifier(EvaluationReasonKind value)
            {
                switch (value)
                {
                    case EvaluationReasonKind.Off:
                        return "OFF";
                    case EvaluationReasonKind.Fallthrough:
                        return "FALLTHROUGH";
                    case EvaluationReasonKind.TargetMatch:
                        return "TARGET_MATCH";
                    case EvaluationReasonKind.RuleMatch:
                        return "RULE_MATCH";
                    case EvaluationReasonKind.PrerequisiteFailed:
                        return "PREREQUISITE_FAILED";
                    case EvaluationReasonKind.Error:
                        return "ERROR";
                    default:
                        throw new ArgumentException();
                }
            }
        }

        /// <summary>
        /// The JSON converter for <see cref="LdValue"/>.
        /// </summary>
        public sealed class LdValueConverter : JsonConverter<LdValue>
        {
            public override void Write(Utf8JsonWriter writer, LdValue value, JsonSerializerOptions options) =>
                WriteJsonValue(value, writer);

            public override LdValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                ReadJsonValue(ref reader);

            public static void WriteJsonValue(LdValue value, Utf8JsonWriter writer)
            {
                switch (value.Type)
                {
                    case LdValueType.Null:
                        writer.WriteNullValue();
                        break;
                    case LdValueType.Bool:
                        writer.WriteBooleanValue(value.AsBool);
                        break;
                    case LdValueType.Number:
                        var asInt = value.AsInt;
                        var asDouble = value.AsDouble;
                        if ((double)asInt == asDouble)
                        {
                            writer.WriteNumberValue(asInt);
                        }
                        else
                        {
                            writer.WriteNumberValue(asDouble);
                        }
                        break;
                    case LdValueType.String:
                        writer.WriteStringValue(value.AsString);
                        break;
                    case LdValueType.Array:
                        writer.WriteStartArray();
                        foreach (var v in value.List)
                        {
                            WriteJsonValue(v, writer);
                        }
                        writer.WriteEndArray();
                        break;
                    case LdValueType.Object:
                        writer.WriteStartObject();
                        foreach (var kv in value.Dictionary)
                        {
                            writer.WritePropertyName(kv.Key);
                            WriteJsonValue(kv.Value, writer);
                        }
                        writer.WriteEndObject();
                        break;
                }
            }

            public static LdValue ReadJsonValue(ref Utf8JsonReader reader)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.True:
                        return LdValue.Of(true);
                    case JsonTokenType.False:
                        return LdValue.Of(false);
                    case JsonTokenType.Number:
                        return LdValue.Of(reader.GetDouble());
                    case JsonTokenType.String:
                        return LdValue.Of(reader.GetString());
                    case JsonTokenType.StartArray:
                        var arrayBuilder = LdValue.BuildArray();
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }
                            arrayBuilder.Add(ReadJsonValue(ref reader));
                        }
                        return arrayBuilder.Build();
                    case JsonTokenType.StartObject:
                        var objectBuilder = LdValue.BuildObject();
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            var name = reader.GetString();
                            reader.Read();
                            objectBuilder.Add(name, ReadJsonValue(ref reader));
                        }
                        return objectBuilder.Build();
                    default:
                        return LdValue.Null;
                }
            }
        }

        /// <summary>
        /// The JSON converter for <see cref="UnixMillisecondTime"/>.
        /// </summary>
        public sealed class UnixMillisecondTimeConverter: JsonConverter<UnixMillisecondTime>
        {
            public override void Write(Utf8JsonWriter writer, UnixMillisecondTime value, JsonSerializerOptions options) =>
                writer.WriteNumberValue(value.Value);

            public override UnixMillisecondTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                UnixMillisecondTime.OfMillis(reader.GetInt64());
        }
    }
#pragma warning restore CS1591
}
