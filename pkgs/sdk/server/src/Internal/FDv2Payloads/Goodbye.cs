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

        /// <summary>
        /// Whether the protocolFallbackTTL field was present. Its presence is an FDv1 Fallback Directive.
        /// </summary>
        public bool HasProtocolFallbackTtl { get; }

        /// <summary>
        /// The protocolFallbackTTL value in seconds, or null if the field was absent or not a number.
        /// </summary>
        public double? ProtocolFallbackTtl { get; }

        /// <summary>
        /// Constructs a new Goodbye.
        /// </summary>
        /// <param name="reason">Reason for the disconnection.</param>
        /// <param name="hasProtocolFallbackTtl">Whether the protocolFallbackTTL field was present.</param>
        /// <param name="protocolFallbackTtl">The protocolFallbackTTL value in seconds, if it was a number.</param>
        public Goodbye(string reason, bool hasProtocolFallbackTtl = false, double? protocolFallbackTtl = null)
        {
            Reason = reason;
            HasProtocolFallbackTtl = hasProtocolFallbackTtl;
            ProtocolFallbackTtl = protocolFallbackTtl;
        }
    }

    /// <summary>
    /// JsonConverter for Goodbye events.
    /// </summary>
    internal sealed class GoodbyeConverter : JsonConverter<Goodbye>
    {
        private const string AttributeReason = "reason";
        private const string AttributeProtocolFallbackTtl = "protocolFallbackTTL";

        internal static readonly GoodbyeConverter Instance = new GoodbyeConverter();

        public override Goodbye Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string reason = null;
            var hasProtocolFallbackTtl = false;
            double? protocolFallbackTtl = null;

            for (var obj = RequireObject(ref reader); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case AttributeReason:
                        reason = reader.GetString();
                        break;
                    case AttributeProtocolFallbackTtl:
                        hasProtocolFallbackTtl = true;
                        if (reader.TokenType == JsonTokenType.Number)
                        {
                            protocolFallbackTtl = reader.GetDouble();
                        }
                        else
                        {
                            reader.Skip();
                        }
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new Goodbye(reason, hasProtocolFallbackTtl, protocolFallbackTtl);
        }

        public override void Write(Utf8JsonWriter writer, Goodbye value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value.Reason != null)
            {
                writer.WriteString(AttributeReason, value.Reason);
            }

            if (value.ProtocolFallbackTtl.HasValue)
            {
                writer.WriteNumber(AttributeProtocolFallbackTtl, value.ProtocolFallbackTtl.Value);
            }

            writer.WriteEndObject();
        }
    }
}
