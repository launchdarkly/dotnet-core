using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2Payloads
{
    /// <summary>
    /// Represents the intent code indicating how the server intends to transfer data.
    /// </summary>
    [JsonConverter(typeof(IntentCodeConverter))]
    internal enum IntentCode
    {
        /// <summary>
        /// Indicates no changes are needed; the client is already up to date.
        /// </summary>
        None,

        /// <summary>
        /// Indicates a full data transfer will be performed.
        /// </summary>
        TransferFull,

        /// <summary>
        /// Indicates incremental changes will be sent.
        /// </summary>
        TransferChanges
    }

    /// <summary>
    /// Constants for intent code string values.
    /// </summary>
    internal static class IntentCodeConstants
    {
        public const string None = "none";
        public const string TransferFull = "xfer-full";
        public const string TransferChanges = "xfer-changes";
    }

    /// <summary>
    /// Extension methods for IntentCode.
    /// </summary>
    internal static class IntentCodeExtensions
    {
        /// <summary>
        /// Converts an IntentCode to its string representation.
        /// </summary>
        public static string ToStringValue(this IntentCode intentCode)
        {
            switch (intentCode)
            {
                case IntentCode.None:
                    return IntentCodeConstants.None;
                case IntentCode.TransferFull:
                    return IntentCodeConstants.TransferFull;
                case IntentCode.TransferChanges:
                    return IntentCodeConstants.TransferChanges;
                default:
                    // This represents a mistake with implementation. The enum was extended, but support for
                    // string conversion was not.
                    throw new ArgumentOutOfRangeException(nameof(intentCode), intentCode, "Unknown intent code");
            }
        }

        /// <summary>
        /// Parses a string into an IntentCode.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the value is not a valid intent code.</exception>
        public static IntentCode ParseIntentCode(string value)
        {
            switch (value)
            {
                case IntentCodeConstants.None:
                    return IntentCode.None;
                case IntentCodeConstants.TransferFull:
                    return IntentCode.TransferFull;
                case IntentCodeConstants.TransferChanges:
                    return IntentCode.TransferChanges;
                default:
                    throw new ArgumentException($"Unknown intent code: {value}", nameof(value));
            }
        }
    }

    /// <summary>
    /// JSON converter for IntentCode that handles string conversion.
    /// </summary>
    internal sealed class IntentCodeConverter : JsonConverter<IntentCode>
    {
        public override IntentCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            try
            {
                return IntentCodeExtensions.ParseIntentCode(value);
            }
            catch (ArgumentException ex)
            {
                throw new JsonException($"Unknown intent code: {value}", ex);
            }
        }

        public override void Write(Utf8JsonWriter writer, IntentCode value, JsonSerializerOptions options)
        {
            try
            {
                var stringValue = value.ToStringValue();
                writer.WriteStringValue(stringValue);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JsonException($"Unknown intent code: {value}", ex);
            }
        }
    }
}
