using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Represents the goodbye event, which indicates that the server is about to disconnect.
    /// </summary>
    internal sealed class Goodbye
    {
        /// <summary>
        /// Reason for the disconnection.
        /// </summary>
        public string Reason { get; }

        public Goodbye(string reason)
        {
            Reason = reason;
        }
    }

    /// <summary>
    /// JsonConverter for Goodbye events.
    /// </summary>
    internal sealed class GoodbyeConverter : JsonConverter<Goodbye>
    {
        private const string AttributeReason = "reason";

        internal static readonly GoodbyeConverter Instance = new GoodbyeConverter();

        public override Goodbye Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string reason = null;

            for (var obj = RequireObject(ref reader); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case AttributeReason:
                        reason = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new Goodbye(reason);
        }

        public override void Write(Utf8JsonWriter writer, Goodbye value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value.Reason != null)
            {
                writer.WriteString(AttributeReason, value.Reason);
            }

            writer.WriteEndObject();
        }
    }
}
