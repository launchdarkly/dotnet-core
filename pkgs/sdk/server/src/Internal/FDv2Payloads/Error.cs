using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Represents the error event, which indicates an error encountered server-side affecting the payload transfer.
    /// SDKs must discard partially transferred data. The SDK remains connected and expects the server to recover.
    /// </summary>
    internal sealed class Error
    {
        /// <summary>
        /// The unique string identifier of the entity the error relates to.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Human-readable reason the error occurred.
        /// </summary>
        public string Reason { get; }

        public Error(string id, string reason)
        {
            Id = id;
            Reason = reason;
        }
    }

    /// <summary>
    /// JsonConverter for Error events.
    /// </summary>
    internal sealed class ErrorConverter : JsonConverter<Error>
    {
        private const string AttributeId = "id";
        private const string AttributeReason = "reason";

        internal static readonly ErrorConverter Instance = new ErrorConverter();
        private static readonly string[] RequiredProperties = { AttributeReason };

        public override Error Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string id = null;
            string reason = null;

            for (var obj = RequireObject(ref reader).WithRequiredProperties(RequiredProperties); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case AttributeId:
                        id = reader.GetString();
                        break;
                    case AttributeReason:
                        reason = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new Error(id, reason);
        }

        public override void Write(Utf8JsonWriter writer, Error value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value.Id != null)
            {
                writer.WriteString(AttributeId, value.Id);
            }

            if (value.Reason != null)
            {
                writer.WriteString(AttributeReason, value.Reason);
            }

            writer.WriteEndObject();
        }
    }
}
