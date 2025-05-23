using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchDarkly.Sdk.Json
{
    public static partial class LdJsonConverters
    {
#pragma warning disable CS1591 // don't bother with XML comments for these low-level helpers
        /// <summary>
        /// The JSON converter for <see cref="User"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Applications should not need to use this class directly. It is used automatically in
        /// <c>System.Text.Json</c> conversion, or if you call <see cref="LdJsonSerialization.SerializeObject{T}(T)"/>
        /// or <see cref="LdJsonSerialization.DeserializeObject{T}(string)"/>
        /// </para>
        /// <para>
        /// LaunchDarkly's JSON schema for users is standardized across SDKs. It corresponds to the
        /// <see cref="User"/> model, rather than the richer <see cref="Context"/> model; any JSON
        /// representation of a <see cref="User"/> can also be decoded as a <see cref="Context"/>,
        /// but not vice versa.
        /// </para>
        /// </remarks>
        public sealed class UserConverter : JsonConverter<User>
        {
            internal const string JsonPropCustom = "custom";
            internal const string JsonPropPrivateAttributeNames = "privateAttributeNames";

            public override User Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                RequireToken(ref reader, JsonTokenType.StartObject);
                var builder = User.Builder("") as UserBuilder; // casting to concrete type so we can get internal methods
                string key = null;
                while (NextProperty(ref reader, out var name))
                {
                    switch (name)
                    {
                        case "key":
                            builder.Key(key = reader.GetString());
                            break;
                        case "ip":
                            builder.IPAddress(reader.GetString());
                            break;
                        case "country":
                            builder.Country(reader.GetString());
                            break;
                        case "firstName":
                            builder.FirstName(reader.GetString());
                            break;
                        case "lastName":
                            builder.LastName(reader.GetString());
                            break;
                        case "name":
                            builder.Name(reader.GetString());
                            break;
                        case "avatar":
                            builder.Avatar(reader.GetString());
                            break;
                        case "email":
                            builder.Email(reader.GetString());
                            break;
                        case "anonymous":
                            builder.Anonymous(!ConsumeNull(ref reader) && reader.GetBoolean());
                            break;
                        case "custom":
                            if (!ConsumeNull(ref reader))
                            {
                                RequireToken(ref reader, JsonTokenType.StartObject, name);
                                while (NextProperty(ref reader, out var propName))
                                {
                                    builder.Custom(propName, LdValueConverter.ReadJsonValue(ref reader));
                                }
                            }
                            break;
                        case "privateAttributeNames":
                            if (!ConsumeNull(ref reader))
                            {
                                RequireToken(ref reader, JsonTokenType.StartArray, name);
                                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                {
                                    builder.AddPrivateAttribute(reader.GetString());
                                }
                            }
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                if (key is null)
                {
                    throw new JsonException(Errors.JsonMissingProperty("key"));
                }
                return builder.Build();
            }

            public override void Write(Utf8JsonWriter writer, User u, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("key", u.Key);
                foreach (UserAttribute a in UserAttribute.OptionalStringAttrs)
                {
                    var value = u.GetAttribute(a);
                    if (!value.IsNull)
                    {
                        writer.WriteString(a.AttributeName, value.AsString);
                    }
                }
                if (u.Anonymous)
                {
                    writer.WriteBoolean("anonymous", true);
                }
                if (u.Custom.Count != 0)
                {
                    writer.WriteStartObject(JsonPropCustom);
                    foreach (var kv in u.Custom)
                    {
                        writer.WritePropertyName(kv.Key);
                        LdValueConverter.WriteJsonValue(kv.Value, writer);
                    }
                    writer.WriteEndObject();
                }
                if (u.PrivateAttributeNames.Count != 0)
                {
                    writer.WriteStartArray(JsonPropPrivateAttributeNames);
                    foreach (var pa in u.PrivateAttributeNames)
                    {
                        writer.WriteStringValue(pa);
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
        }
    }
#pragma warning restore CS1591
}
