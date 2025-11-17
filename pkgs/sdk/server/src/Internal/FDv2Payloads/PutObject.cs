using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Represents the put-object event, which contains a payload object that should be accepted with upsert semantics.
    /// The object can be either a flag or a segment.
    /// </summary>
    internal sealed class PutObject
    {
        /// <summary>
        /// The minimum payload version this change applies to. May not match target version of ServerIntent message.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// The kind of the object being PUT ("flag" or "segment").
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// The identifier of the object.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The raw JSON element representing the object being PUT (flag or segment).
        /// This will be deserialized separately based on the Kind.
        /// </summary>
        public JsonElement Object { get; }

        public PutObject(int version, string kind, string key, JsonElement obj)
        {
            Version = version;
            Kind = kind;
            Key = key;
            Object = obj;
        }
    }

    /// <summary>
    /// JsonConverter for PutObject events.
    /// </summary>
    internal sealed class PutObjectConverter : JsonConverter<PutObject>
    {
        private const string AttributeVersion = "version";
        private const string AttributeKind = "kind";
        private const string AttributeKey = "key";
        private const string AttributeObject = "object";

        internal static readonly PutObjectConverter Instance = new PutObjectConverter();

        private static readonly string[] RequiredProperties =
            { AttributeVersion, AttributeKind, AttributeKey, AttributeObject };

        public override PutObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var version = 0;
            string kind = null;
            string key = null;
            JsonElement obj = default;

            for (var objIter = RequireObject(ref reader).WithRequiredProperties(RequiredProperties);
                 objIter.Next(ref reader);)
            {
                switch (objIter.Name)
                {
                    case AttributeVersion:
                        version = reader.GetInt32();
                        break;
                    case AttributeKind:
                        kind = reader.GetString();
                        break;
                    case AttributeKey:
                        key = reader.GetString();
                        break;
                    case AttributeObject:
                        // Store the raw JSON element for later deserialization
                        obj = JsonElement.ParseValue(ref reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new PutObject(version, kind, key, obj);
        }

        public override void Write(Utf8JsonWriter writer, PutObject value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber(AttributeVersion, value.Version);
            if (value.Kind != null)
            {
                writer.WriteString(AttributeKind, value.Kind);
            }

            if (value.Key != null)
            {
                writer.WriteString(AttributeKey, value.Key);
            }

            writer.WritePropertyName(AttributeObject);
            value.Object.WriteTo(writer);
            writer.WriteEndObject();
        }
    }
}
