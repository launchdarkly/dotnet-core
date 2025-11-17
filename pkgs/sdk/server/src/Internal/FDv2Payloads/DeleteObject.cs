using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Represents the delete-object event, which contains a payload object that should be deleted.
    /// </summary>
    internal sealed class DeleteObject
    {
        /// <summary>
        /// The minimum payload version this change applies to. May not match the target version of ServerIntent message.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// The kind of the object being deleted ("flag" or "segment").
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// The identifier of the object.
        /// </summary>
        public string Key { get; }

        public DeleteObject(int version, string kind, string key)
        {
            Version = version;
            Kind = kind;
            Key = key;
        }
    }

    /// <summary>
    /// JsonConverter for DeleteObject events.
    /// </summary>
    internal sealed class DeleteObjectConverter : JsonConverter<DeleteObject>
    {
        private const string AttributeVersion = "version";
        private const string AttributeKind = "kind";
        private const string AttributeKey = "key";

        internal static readonly DeleteObjectConverter Instance = new DeleteObjectConverter();

        private static readonly string[] RequiredProperties =
            { AttributeVersion, AttributeKind, AttributeKey };

        public override DeleteObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var version = 0;
            string kind = null;
            string key = null;

            for (var obj = RequireObject(ref reader).WithRequiredProperties(RequiredProperties); obj.Next(ref reader);)
            {
                switch (obj.Name)
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
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new DeleteObject(version, kind, key);
        }

        public override void Write(Utf8JsonWriter writer, DeleteObject value, JsonSerializerOptions options)
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

            writer.WriteEndObject();
        }
    }
}
