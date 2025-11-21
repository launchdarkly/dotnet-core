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
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// The identifier of the object.
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The raw JSON string representing the object being PUT (flag or segment).
        /// <para>
        /// This will be deserialized separately based on the Kind.
        /// </para>
        /// <para>
        /// This field is required.
        /// </para>
        /// </summary>
        public string Object { get; }

        /// <summary>
        /// Constructs a new PutObject.
        /// </summary>
        /// <param name="version">The minimum payload version this change applies to.</param>
        /// <param name="kind">The kind of object being PUT ("flag" or "segment").</param>
        /// <param name="key">The identifier of the object.</param>
        /// <param name="obj">The raw JSON string representing the object being PUT.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="kind"/>, <paramref name="key"/>, or <paramref name="obj"/> is null.
        /// </exception>
        public PutObject(int version, string kind, string key, string obj)
        {
            Version = version;
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Object = obj ?? throw new ArgumentNullException(nameof(obj));
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
            string obj = null;

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
                        // Store the raw JSON string for later deserialization
                        var element = JsonElement.ParseValue(ref reader);
                        obj = element.GetRawText();
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
            writer.WriteString(AttributeKind, value.Kind);
            writer.WriteString(AttributeKey, value.Key);
            writer.WritePropertyName(AttributeObject);
            writer.WriteRawValue(value.Object);
            writer.WriteEndObject();
        }
    }
}
