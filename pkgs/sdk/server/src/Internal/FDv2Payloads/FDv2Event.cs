using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Server.Internal.FDv2DataSources;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Exception thrown when attempting to deserialize an FDv2Event as the wrong event type.
    /// </summary>
    internal sealed class FDv2EventTypeMismatchException : Exception
    {
        /// <summary>
        /// The actual event type of the FDv2Event.
        /// </summary>
        public string ActualEventType { get; }

        /// <summary>
        /// The expected event type for deserialization.
        /// </summary>
        public string ExpectedEventType { get; }

        public FDv2EventTypeMismatchException(string actualEventType, string expectedEventType)
            : base($"Cannot deserialize event type '{actualEventType}' as '{expectedEventType}'.")
        {
            ActualEventType = actualEventType;
            ExpectedEventType = expectedEventType;
        }
    }

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
        public JsonElement? Data { get; }

        public FDv2Event(string eventType, JsonElement? data)
        {
            EventType = eventType;
            Data = data;
        }

        /// <summary>
        /// Deserializes the Data element as a ServerIntent.
        /// </summary>
        /// <returns>The deserialized ServerIntent.</returns>
        /// <exception cref="FDv2EventTypeMismatchException">
        /// Thrown when the event type is not "server-intent".
        /// </exception>
        /// <exception cref="JsonException">
        /// Thrown when the JSON data cannot be deserialized as a ServerIntent.
        /// </exception>
        public ServerIntent AsServerIntent()
        {
            return DeserializeAs<ServerIntent>(FDv2EventTypes.ServerIntent);
        }

        /// <summary>
        /// Deserializes the Data element as a PutObject.
        /// </summary>
        /// <returns>The deserialized PutObject.</returns>
        /// <exception cref="FDv2EventTypeMismatchException">
        /// Thrown when the event type is not "put-object".
        /// </exception>
        /// <exception cref="JsonException">
        /// Thrown when the JSON data cannot be deserialized as a PutObject.
        /// </exception>
        public PutObject AsPutObject()
        {
            return DeserializeAs<PutObject>(FDv2EventTypes.PutObject);
        }

        /// <summary>
        /// Deserializes the Data element as a DeleteObject.
        /// </summary>
        /// <returns>The deserialized DeleteObject.</returns>
        /// <exception cref="FDv2EventTypeMismatchException">
        /// Thrown when the event type is not "delete-object".
        /// </exception>
        /// <exception cref="JsonException">
        /// Thrown when the JSON data cannot be deserialized as a DeleteObject.
        /// </exception>
        public DeleteObject AsDeleteObject()
        {
            return DeserializeAs<DeleteObject>(FDv2EventTypes.DeleteObject);
        }

        /// <summary>
        /// Deserializes the Data element as a PayloadTransferred.
        /// </summary>
        /// <returns>The deserialized PayloadTransferred.</returns>
        /// <exception cref="FDv2EventTypeMismatchException">
        /// Thrown when the event type is not "payload-transferred".
        /// </exception>
        /// <exception cref="JsonException">
        /// Thrown when the JSON data cannot be deserialized as a PayloadTransferred.
        /// </exception>
        public PayloadTransferred AsPayloadTransferred()
        {
            return DeserializeAs<PayloadTransferred>(FDv2EventTypes.PayloadTransferred);
        }

        /// <summary>
        /// Deserializes the Data element as an Error.
        /// </summary>
        /// <returns>The deserialized Error.</returns>
        /// <exception cref="FDv2EventTypeMismatchException">
        /// Thrown when the event type is not "error".
        /// </exception>
        /// <exception cref="JsonException">
        /// Thrown when the JSON data cannot be deserialized as an Error.
        /// </exception>
        public Error AsError()
        {
            return DeserializeAs<Error>(FDv2EventTypes.Error);
        }

        /// <summary>
        /// Deserializes the Data element as a Goodbye.
        /// </summary>
        /// <returns>The deserialized Goodbye.</returns>
        /// <exception cref="FDv2EventTypeMismatchException">
        /// Thrown when the event type is not "goodbye".
        /// </exception>
        /// <exception cref="JsonException">
        /// Thrown when the JSON data cannot be deserialized as a Goodbye.
        /// </exception>
        public Goodbye AsGoodbye()
        {
            return DeserializeAs<Goodbye>(FDv2EventTypes.Goodbye);
        }

        /// <summary>
        /// Helper method to deserialize the Data element as the specified type.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="expectedEventType">The expected event type string.</param>
        /// <returns>The deserialized object of type T.</returns>
        /// <exception cref="FDv2EventTypeMismatchException">
        /// Thrown when the actual event type doesn't match the expected event type.
        /// </exception>
        /// <exception cref="JsonException">
        /// Thrown when the JSON data cannot be deserialized as type T.
        /// </exception>
        private T DeserializeAs<T>(string expectedEventType)
        {
            if (EventType != expectedEventType)
            {
                throw new FDv2EventTypeMismatchException(EventType, expectedEventType);
            }

            if (!Data.HasValue)
            {
                throw new JsonException($"Failed to deserialize {expectedEventType} event data. Data was not present.");
            }

            try
            {
                return JsonSerializer.Deserialize<T>(Data.Value.GetRawText(), GetSerializerOptions());
            }
            catch (JsonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new JsonException($"Failed to deserialize {expectedEventType} event data.", ex);
            }
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
        private static readonly string[] RequiredProperties = { AttributeEvent, AttributeData };

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
            if (!value.Data.HasValue)
            {
                throw new JsonException($"Failed to write {value.EventType} to event.");
            }

            writer.WriteStartObject();
            if (value.EventType != null)
            {
                writer.WriteString(AttributeEvent, value.EventType);
            }

            writer.WritePropertyName(AttributeData);
            value.Data.Value.WriteTo(writer);
            writer.WriteEndObject();
        }
    }
}
