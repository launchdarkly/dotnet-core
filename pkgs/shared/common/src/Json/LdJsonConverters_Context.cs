using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchDarkly.Sdk.Json
{
    public static partial class LdJsonConverters
    {
#pragma warning disable CS1591 // don't bother with XML comments for these low-level helpers
        /// <summary>
        /// The JSON converter for <see cref="Context"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Applications should not need to use this class directly. It is used automatically in
        /// <c>System.Text.Json</c> conversion, or if you call <see cref="LdJsonSerialization.SerializeObject{T}(T)"/>
        /// or <see cref="LdJsonSerialization.DeserializeObject{T}(string)"/>
        /// </para>
        /// <para>
        /// LaunchDarkly's JSON schema for contexts is standardized across SDKs. There are two serialization
        /// formats, depending on whether it is a single-kind context or a multi-kind context. There is also
        /// a third format corresponding to how users were represented in JSON in older LaunchDarkly SDKs;
        /// this format is recognized automatically and supported for deserialization, but is not supported
        /// for serialization.
        /// </para>
        /// </remarks>
        public sealed class ContextConverter : JsonConverter<Context>
        {
            private const string AttrKind = "kind";
            internal const string AttrKey = "key"; // also used by UserConverter
            private const string AttrName = "name";
            internal const string AttrAnonymous = "anonymous"; // also used by UserConverter
            private const string JsonPropMeta = "_meta";
            private const string JsonPropPrivateAttributes = "privateAttributes";

            public override Context Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                // The implementation here of unmarshaling a context/user is that we first unmarshal the
                // whole JSON object into an LdValue (as a convenient way to represent arbitrary JSON data
                // structures), and then decide how to translate that object into a Context. This is
                // somewhat inefficient because we're producing dictionary-like structures that we don't
                // intend to keep-- but a straight-ahead approach, where we would parse JSON tokens as a
                // stream, is not really possible due to the details of the context JSON schema (we can't
                // know what schema we're using until we see the "kind" property). And, unmarshaling context
                // data from JSON is not a task applications are likely to be doing frequently enough for
                // it to be performance-critical.
                var objValue = LdValueConverter.ReadJsonValue(ref reader);
                if (objValue.Dictionary.TryGetValue(AttrKind, out var kindValue))
                {
                    if (!kindValue.IsString)
                    {
                        throw WrongType(kindValue, AttrKind);
                    }
                    if (kindValue.AsString == ContextKind.Multi.Value)
                    {
                        return ReadJsonMulti(objValue);
                    }
                    return ReadJsonSingle(objValue, null);
                }
                return ReadJsonOldUser(objValue);
            }

            public override void Write(Utf8JsonWriter writer, Context c, JsonSerializerOptions options)
            {
                if (!(c.Error is null))
                {
                    throw new JsonException(Errors.JsonSerializeInvalidContext(c.Error));
                }
                if (c.Multiple)
                {
                    writer.WriteStartObject();
                    writer.WriteString(AttrKind, ContextKind.Multi.Value);
                    foreach (var mc in c._multiContexts)
                    {
                        writer.WritePropertyName(mc.Kind.Value);
                        WriteJsonSingle(mc, writer, false);
                    }
                    writer.WriteEndObject();
                }
                else
                {
                    WriteJsonSingle(c, writer, true);
                }
            }

            private Context ReadJsonSingle(LdValue objValue, string knownKind)
            {
                var builder = Context.Builder("").Kind(knownKind);
                var kind = knownKind;
                foreach (var kv in objValue.Dictionary)
                {
                    if (kv.Key == JsonPropMeta)
                    {
                        RequireType(kv.Value, LdValueType.Object, true, JsonPropMeta);
                        var meta = kv.Value.Dictionary;
                        if (meta.TryGetValue(JsonPropPrivateAttributes, out var privateAttrs))
                        {
                            RequireType(privateAttrs, LdValueType.Array, true, "{0}.{1}", JsonPropMeta, JsonPropPrivateAttributes);
                            for (int i = 0; i < privateAttrs.List.Count; i++)
                            {
                                var value = privateAttrs.List[i];
                                RequireType(value, LdValueType.String, false, "{0}.{1}[{2}]", JsonPropMeta, JsonPropPrivateAttributes, i);
                                builder.Private(value.AsString);
                            }
                        }
                    }
                    else
                    {
                        if (!builder.TrySet(kv.Key, kv.Value))
                        {
                            throw WrongType(kv.Value, kv.Key);
                        }
                        if (kv.Key == AttrKind)
                        {
                            kind = kv.Value.AsString;
                        }
                    }
                }
                if (kind is null)
                {
                    throw new JsonException(Errors.JsonMissingProperty(AttrKind));
                }
                if (kind == "")
                {
                    throw new JsonException(Errors.JsonContextEmptyKind);
                }
                return Validate(builder.Build());
            }

            private Context Validate(Context c) =>
                c.Error is null ? c : throw new JsonException(c.Error);

            private Context ReadJsonMulti(LdValue objValue)
            {
                var builder = Context.MultiBuilder();
                foreach (var kv in objValue.Dictionary)
                {
                    if (kv.Key != "kind")
                    {
                        builder.Add(ReadJsonSingle(kv.Value, kv.Key));
                    }
                }
                return Validate(builder.Build());
            }

            private Context ReadJsonOldUser(LdValue objValue)
            {
                var builder = Context.Builder("");
                builder.AllowEmptyKey(true);
                var hasKey = false;

                foreach (var kv in objValue.Dictionary)
                {
                    switch (kv.Key)
                    {
                        case AttrAnonymous:
                            RequireType(kv.Value, LdValueType.Bool, true, AttrAnonymous);
                            builder.Anonymous(kv.Value.AsBool);
                            break;

                        case UserConverter.JsonPropCustom:
                            RequireType(kv.Value, LdValueType.Object, true, UserConverter.JsonPropCustom);
                            foreach (var kv1 in kv.Value.Dictionary)
                            {
                                switch (kv1.Key)
                                {
                                    // can't allow an old-style custom attribute to overwrite a top-level one with the same name
                                    case AttrKind:
                                    case AttrKey:
                                    case AttrName:
                                    case AttrAnonymous:
                                    case JsonPropMeta:
                                        break;
                                    default:
                                        builder.Set(kv1.Key, kv1.Value);
                                        break;
                                }
                            }
                            break;

                        case UserConverter.JsonPropPrivateAttributeNames:
                            RequireType(kv.Value, LdValueType.Array, true, UserConverter.JsonPropPrivateAttributeNames);
                            for (int i = 0; i < kv.Value.List.Count; i++)
                            {
                                var value = kv.Value.List[i];
                                RequireType(value, LdValueType.String, false, "{0}[{1}]", UserConverter.JsonPropPrivateAttributeNames, i);
                                builder.Private(AttributeRef.FromLiteral(value.AsString));
                            }
                            break;

                        case AttrName:
                        case "firstName":
                        case "lastName":
                        case "email":
                        case "country":
                        case "ip":
                        case "avatar":
                            if (!kv.Value.IsString && !kv.Value.IsNull)
                            {
                                throw WrongType(kv.Value, kv.Key);
                            }
                            builder.Set(kv.Key, kv.Value);
                            break;

                        default:
                            if (!builder.TrySet(kv.Key, kv.Value))
                            {
                                throw WrongType(kv.Value, kv.Key);
                            }
                            if (kv.Key == AttrKey)
                            {
                                hasKey = true;
                            }
                            break;
                    }
                }

                if (!hasKey)
                {
                    throw new JsonException(Errors.JsonMissingProperty(AttrKey));
                }
                return Validate(builder.Build());
            }

            private static void RequireType(LdValue value, LdValueType type, bool nullable,
                string propNameFormat, params object[] propNameArgs)
            {
                if (value.Type != type && !(value.IsNull && nullable))
                {
                    throw WrongType(value, string.Format(propNameFormat, propNameArgs));
                }
            }

            private static JsonException WrongType(LdValue value, string name) =>
                new JsonException(Errors.JsonWrongType(name, value.Type));

            private void WriteJsonSingle(in Context c, Utf8JsonWriter writer, bool includeKind)
            {
                writer.WriteStartObject();
                if (includeKind)
                {
                    writer.WriteString(AttrKind, c.Kind.Value);
                }
                writer.WriteString(AttrKey, c.Key);
                if (c.Name != null)
                {
                    writer.WriteString(AttrName, c.Name);
                }
                if (c.Anonymous)
                {
                    writer.WriteBoolean(AttrAnonymous, true);
                }
                if (!(c._attributes is null))
                {
                    foreach (var kv in c._attributes)
                    {
                        writer.WritePropertyName(kv.Key);
                        LdValueConverter.WriteJsonValue(kv.Value, writer);
                    }
                }
                if (!(c._privateAttributes is null))
                {
                    writer.WriteStartObject(JsonPropMeta);
                    writer.WriteStartArray(JsonPropPrivateAttributes);
                    foreach (var pa in c._privateAttributes)
                    {
                        writer.WriteStringValue(pa.ToString());
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
        }
    }
#pragma warning restore CS1591
}
