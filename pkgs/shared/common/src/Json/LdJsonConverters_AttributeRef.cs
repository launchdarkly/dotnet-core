using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchDarkly.Sdk.Json
{
    public static partial class LdJsonConverters
    {
#pragma warning disable CS1591 // don't bother with XML comments for these low-level helpers

        /// <summary>
        /// The JSON converter for <see cref="AttributeRef"/>.
        /// </summary>
        public sealed class AttributeRefConverter : JsonConverter<AttributeRef>
        {
            public override void Write(Utf8JsonWriter writer, AttributeRef value, JsonSerializerOptions options)
            {
                if (value.Defined)
                {
                    writer.WriteStringValue(value.ToString());
                }
                else
                {
                    writer.WriteNullValue();
                }
            }

            public override AttributeRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var maybeString = reader.GetString();
                return maybeString is null ? new AttributeRef() : AttributeRef.FromPath(maybeString);
            }
        }
    }

#pragma warning restore CS1591
}
