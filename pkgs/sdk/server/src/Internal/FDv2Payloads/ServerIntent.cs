using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Represents the server-intent event, which is the first message sent by flag delivery
    /// upon connecting to FDv2. Contains information about how flag delivery intends to handle the payloads.
    /// </summary>
    internal sealed class ServerIntent
    {
        /// <summary>
        /// The list of payloads the server will be transferring data for.
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public ImmutableList<ServerIntentPayload> Payloads { get; }

        /// <summary>
        /// Constructs a new ServerIntent.
        /// </summary>
        /// <param name="payloads">The list of payloads the server will be transferring data for.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="payloads"/> is null.</exception>
        public ServerIntent(ImmutableList<ServerIntentPayload> payloads)
        {
            Payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
        }
    }

    /// <summary>
    /// Description of server intent to transfer a specific payload.
    /// </summary>
    internal sealed class ServerIntentPayload
    {
        /// <summary>
        /// The unique string identifier.
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The target version that the server has determined the client should be on.
        /// Information communicated will be to get the client to that version.
        /// </summary>
        public int Target { get; }

        /// <summary>
        /// Indicates how the server intends to operate with respect to sending payload data.
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public string IntentCode { get; }

        /// <summary>
        /// Reason the server is operating with the provided code.
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Constructs a new ServerIntentPayload.
        /// </summary>
        /// <param name="id">The unique string identifier.</param>
        /// <param name="target">The target version for the payload.</param>
        /// <param name="intentCode">How the server intends to operate with respect to sending payload data.</param>
        /// <param name="reason">Reason the server is operating with the provided code.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/>, <paramref name="intentCode"/>, or <paramref name="reason"/> is null.</exception>
        public ServerIntentPayload(string id, int target, string intentCode, string reason)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Target = target;
            IntentCode = intentCode ?? throw new ArgumentNullException(nameof(intentCode));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }
    }

    /// <summary>
    /// JsonConverter for ServerIntent events.
    /// </summary>
    internal sealed class ServerIntentConverter : JsonConverter<ServerIntent>
    {
        private const string AttributePayloads = "payloads";
        private const string AttributeId = "id";
        private const string AttributeTarget = "target";
        private const string AttributeIntentCode = "intentCode";
        private const string AttributeReason = "reason";

        internal static readonly ServerIntentConverter Instance = new ServerIntentConverter();
        private static readonly string[] RequiredProperties = { AttributePayloads };

        public override ServerIntent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ImmutableList<ServerIntentPayload> payloads = null;

            for (var obj = RequireObject(ref reader).WithRequiredProperties(RequiredProperties); obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case AttributePayloads:
                        var payloadsBuilder = ImmutableList.CreateBuilder<ServerIntentPayload>();
                        for (var arr = RequireArray(ref reader); arr.Next(ref reader);)
                        {
                            payloadsBuilder.Add(ReadServerIntentPayload(ref reader));
                        }

                        payloads = payloadsBuilder.ToImmutable();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new ServerIntent(payloads);
        }

        public override void Write(Utf8JsonWriter writer, ServerIntent value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteStartArray(AttributePayloads);
            foreach (var payload in value.Payloads)
            {
                WriteServerIntentPayload(writer, payload);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static readonly string[] RequiredPayloadProperties =
            { AttributeId, AttributeTarget, AttributeIntentCode, AttributeReason };

        private static ServerIntentPayload ReadServerIntentPayload(ref Utf8JsonReader reader)
        {
            string id = null;
            var target = 0;
            string intentCode = null;
            string reason = null;

            for (var obj = RequireObject(ref reader).WithRequiredProperties(RequiredPayloadProperties);
                 obj.Next(ref reader);)
            {
                switch (obj.Name)
                {
                    case AttributeId:
                        id = reader.GetString();
                        break;
                    case AttributeTarget:
                        target = reader.GetInt32();
                        break;
                    case AttributeIntentCode:
                        intentCode = reader.GetString();
                        break;
                    case AttributeReason:
                        reason = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new ServerIntentPayload(id, target, intentCode, reason);
        }

        private static void WriteServerIntentPayload(Utf8JsonWriter writer, ServerIntentPayload payload)
        {
            writer.WriteStartObject();
            writer.WriteString(AttributeId, payload.Id);
            writer.WriteNumber(AttributeTarget, payload.Target);
            writer.WriteString(AttributeIntentCode, payload.IntentCode);
            writer.WriteString(AttributeReason, payload.Reason);
            writer.WriteEndObject();
        }
    }
}
