using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Represents the payload-transferred event, which is sent after all messages for a payload update have been transmitted.
    /// </summary>
    internal sealed class PayloadTransferred
    {
        /// <summary>
        /// The unique string representing the payload state.
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public string State { get; }

        /// <summary>
        /// The version of the payload that was transferred to the client.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Constructs a new PayloadTransferred.
        /// </summary>
        /// <param name="state">The unique string representing the payload state.</param>
        /// <param name="version">The version of the payload that was transferred to the client.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
        public PayloadTransferred(string state, int version)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Version = version;
        }
    }

    /// <summary>
    /// JsonConverter for PayloadTransferred events.
    /// </summary>
    internal sealed class PayloadTransferredConverter : JsonConverter<PayloadTransferred>
    {
        private const string AttributeState = "state";
        private const string AttributeVersion = "version";

        internal static readonly PayloadTransferredConverter Instance = new PayloadTransferredConverter();
        private static readonly string[] RequiredProperties = { AttributeState, AttributeVersion };

        public override PayloadTransferred Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            string state = null;
            var version = 0;

            for (var obj = RequireObject(ref reader).WithRequiredProperties(RequiredProperties); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case AttributeState:
                        state = reader.GetString();
                        break;
                    case AttributeVersion:
                        version = reader.GetInt32();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new PayloadTransferred(state, version);
        }

        public override void Write(Utf8JsonWriter writer, PayloadTransferred value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(AttributeState, value.State);
            writer.WriteNumber(AttributeVersion, value.Version);
            writer.WriteEndObject();
        }
    }
}
