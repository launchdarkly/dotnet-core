using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Represents an FDv2 event. This event may be constructed from an SSE event, or it may be directly serialized/
    /// deserialized from a polling response.
    /// </summary>
    internal sealed class FDv2Event
    {
        /// <summary>
        /// The event type string (e.g., "server-intent", "put-object", "delete-object", etc.).
        /// </summary>
        public string EventType { get; }

        /// <summary>
        /// The raw JSON element representing the event data.
        /// This should be deserialized based on the EventType.
        /// </summary>
        public JsonElement Data { get; }

        public FDv2Event(string eventType, JsonElement data)
        {
            EventType = eventType;
            Data = data;
        }

        /// <summary>
        /// Deserializes the Data element as a ServerIntent.
        /// </summary>
        public ServerIntent AsServerIntent()
        {
            return JsonSerializer.Deserialize<ServerIntent>(Data.GetRawText(), GetSerializerOptions());
        }

        /// <summary>
        /// Deserializes the Data element as a PutObject.
        /// </summary>
        public PutObject AsPutObject()
        {
            return JsonSerializer.Deserialize<PutObject>(Data.GetRawText(), GetSerializerOptions());
        }

        /// <summary>
        /// Deserializes the Data element as a DeleteObject.
        /// </summary>
        public DeleteObject AsDeleteObject()
        {
            return JsonSerializer.Deserialize<DeleteObject>(Data.GetRawText(), GetSerializerOptions());
        }

        /// <summary>
        /// Deserializes the Data element as a PayloadTransferred.
        /// </summary>
        public PayloadTransferred AsPayloadTransferred()
        {
            return JsonSerializer.Deserialize<PayloadTransferred>(Data.GetRawText(), GetSerializerOptions());
        }

        /// <summary>
        /// Deserializes the Data element as an Error.
        /// </summary>
        public Error AsError()
        {
            return JsonSerializer.Deserialize<Error>(Data.GetRawText(), GetSerializerOptions());
        }

        /// <summary>
        /// Deserializes the Data element as a Goodbye.
        /// </summary>
        public Goodbye AsGoodbye()
        {
            return JsonSerializer.Deserialize<Goodbye>(Data.GetRawText(), GetSerializerOptions());
        }

        private static JsonSerializerOptions GetSerializerOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(ServerIntentConverter.Instance);
            options.Converters.Add(PutObjectConverter.Instance);
            options.Converters.Add(DeleteObjectConverter.Instance);
            options.Converters.Add(PayloadTransferredConverter.Instance);
            options.Converters.Add(ErrorConverter.Instance);
            options.Converters.Add(GoodbyeConverter.Instance);
            return options;
        }
    }

    /// <summary>
    /// JsonConverter for FDv2PollEvent wrapper with partial deserialization.
    /// </summary>
    internal sealed class FDv2PollEventConverter : JsonConverter<FDv2Event>
    {
        private const string AttributeEvent = "event";
        private const string AttributeData = "data";

        internal static readonly FDv2PollEventConverter Instance = new FDv2PollEventConverter();
        private static readonly string[] RequiredProperties = new string[] { AttributeEvent, AttributeData };

        public override FDv2Event Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string eventType = null;
            JsonElement data = default;

            for (var obj = RequireObject(ref reader).WithRequiredProperties(RequiredProperties); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case AttributeEvent:
                        eventType = reader.GetString();
                        break;
                    case AttributeData:
                        // Store the raw JSON element for later deserialization based on the event type
                        data = JsonElement.ParseValue(ref reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new FDv2Event(eventType, data);
        }

        public override void Write(Utf8JsonWriter writer, FDv2Event value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value.EventType != null)
            {
                writer.WriteString(AttributeEvent, value.EventType);
            }

            writer.WritePropertyName(AttributeData);
            value.Data.WriteTo(writer);
            writer.WriteEndObject();
        }
    }
}
