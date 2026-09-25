using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Server.Internal.Model;

using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;
using static LaunchDarkly.Sdk.Json.LdJsonConverters;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    /// <summary>
    /// The parsed form of one data file. A document can hold full flag definitions, simplified
    /// flag-key-to-value entries, and segment definitions. Each list keeps the order in which the
    /// file lists its entries.
    /// </summary>
    internal sealed class FileDataDocument
    {
        internal static readonly FileDataDocument Empty = new FileDataDocument(null, null, null);

        internal IReadOnlyList<KeyValuePair<string, FeatureFlag>> Flags { get; }
        internal IReadOnlyList<KeyValuePair<string, LdValue>> FlagValues { get; }
        internal IReadOnlyList<KeyValuePair<string, Segment>> Segments { get; }

        internal FileDataDocument(
            IReadOnlyList<KeyValuePair<string, FeatureFlag>> flags,
            IReadOnlyList<KeyValuePair<string, LdValue>> flagValues,
            IReadOnlyList<KeyValuePair<string, Segment>> segments
            )
        {
            Flags = flags ?? ImmutableList<KeyValuePair<string, FeatureFlag>>.Empty;
            FlagValues = flagValues ?? ImmutableList<KeyValuePair<string, LdValue>>.Empty;
            Segments = segments ?? ImmutableList<KeyValuePair<string, Segment>>.Empty;
        }
    }

    /// <summary>
    /// Parses the file data document format. A document is a JSON object with optional
    /// <c>flags</c>, <c>flagValues</c>, and <c>segments</c> members. An alternate parser can
    /// handle other formats, such as YAML.
    /// </summary>
    internal sealed class FileDataParser
    {
        private readonly Func<string, object> _alternateParser;

        /// <summary>
        /// Constructs a parser.
        /// </summary>
        /// <param name="alternateParser">a function that parses non-JSON content into basic .NET
        /// collections, or null to accept JSON only</param>
        internal FileDataParser(Func<string, object> alternateParser)
        {
            _alternateParser = alternateParser;
        }

        /// <summary>
        /// Parses the content of one file. Throws an exception if the content cannot be parsed.
        /// </summary>
        /// <param name="content">the file content</param>
        /// <returns>the parsed document</returns>
        internal FileDataDocument Parse(string content)
        {
            if (_alternateParser == null)
            {
                return ParseJson(content);
            }
            if (content.Trim().StartsWith("{"))
            {
                try
                {
                    return ParseJson(content);
                }
                catch (Exception)
                {
                    // The content is not valid JSON. The alternate parser gets a chance to parse it.
                }
            }
            // The alternate parser produces the most basic .NET data structure that can represent
            // the file content, using types like Dictionary and String. We convert this into a JSON
            // tree so we can use the JSON deserializer. This is inefficient, but it lets us reuse the
            // data model deserialization logic.
            var o = _alternateParser(content);
            var options = new JsonSerializerOptions();
            options.Converters.Add(new UntypedDictionaryJsonSerializer());
            var asJson = JsonSerializer.Serialize(o, options);
            return ParseJson(asJson);
        }

        private static FileDataDocument ParseJson(string data)
        {
            var r = new Utf8JsonReader(Encoding.UTF8.GetBytes(data));
            return ParseJson(ref r);
        }

        private static FileDataDocument ParseJson(ref Utf8JsonReader r)
        {
            var flagsBuilder = ImmutableList.CreateBuilder<KeyValuePair<string, FeatureFlag>>();
            var flagValuesBuilder = ImmutableList.CreateBuilder<KeyValuePair<string, LdValue>>();
            var segmentsBuilder = ImmutableList.CreateBuilder<KeyValuePair<string, Segment>>();
            for (var obj = RequireObject(ref r); obj.Next(ref r);)
            {
                switch (obj.Name)
                {
                    case "flags":
                        for (var subObj = RequireObjectOrNull(ref r); subObj.Next(ref r);)
                        {
                            var key = subObj.Name;
                            var flag = FeatureFlagSerialization.Instance.Read(ref r, null, null);
                            flagsBuilder.Add(new KeyValuePair<string, FeatureFlag>(key, flag));
                        }
                        break;

                    case "flagValues":
                        for (var subObj = RequireObjectOrNull(ref r); subObj.Next(ref r);)
                        {
                            var key = subObj.Name;
                            var value = LdValueConverter.ReadJsonValue(ref r);
                            flagValuesBuilder.Add(new KeyValuePair<string, LdValue>(key, value));
                        }
                        break;

                    case "segments":
                        for (var subObj = RequireObjectOrNull(ref r); subObj.Next(ref r);)
                        {
                            var key = subObj.Name;
                            var segment = SegmentSerialization.Instance.Read(ref r, null, null);
                            segmentsBuilder.Add(new KeyValuePair<string, Segment>(key, segment));
                        }
                        break;
                }
            }
            return new FileDataDocument(flagsBuilder.ToImmutable(), flagValuesBuilder.ToImmutable(),
                segmentsBuilder.ToImmutable());
        }

        /// <summary>
        /// Returns a copy of the flag with the given version, or the same flag if the version
        /// already matches.
        /// </summary>
        internal static FeatureFlag FlagWithVersion(FeatureFlag flag, int version) =>
            flag.Version == version ? flag :
            new FeatureFlag(
                flag.Key,
                version,
                flag.Deleted, flag.On, flag.Prerequisites, flag.Targets, flag.ContextTargets, flag.Rules, flag.Fallthrough,
                flag.OffVariation, flag.Variations, flag.Salt, flag.TrackEvents, flag.TrackEventsFallthrough,
                flag.DebugEventsUntilDate, flag.ClientSide, flag.SamplingRatio, flag.ExcludeFromSummaries, flag.Migration);

        /// <summary>
        /// Returns a copy of the segment with the given version, or the same segment if the version
        /// already matches.
        /// </summary>
        internal static Segment SegmentWithVersion(Segment segment, int version) =>
            segment.Version == version ? segment :
            new Segment(
                segment.Key,
                version,
                segment.Deleted, segment.Included, segment.Excluded, segment.IncludedContexts, segment.ExcludedContexts,
                segment.Rules, segment.Salt, segment.Unbounded, segment.UnboundedContextKind, segment.Generation);

        /// <summary>
        /// Constructs a flag that is on and returns the same value for every context. The flag has
        /// a single variation and a fallthrough to that variation. This is the form the file data
        /// source has always used for <c>flagValues</c> entries.
        /// </summary>
        internal static FeatureFlag MakeFallthroughFlagWithValue(string key, LdValue value, int version)
        {
            var json = LdValue.BuildObject()
                .Add("key", key)
                .Add("version", version)
                .Add("on", true)
                .Add("variations", LdValue.ArrayOf(value))
                .Add("fallthrough", LdValue.BuildObject().Add("variation", 0).Build())
                .Build()
                .ToJsonString();
            return DataModel.Features.Deserialize(json).Item as FeatureFlag;
        }

        // This custom JSON serializer addresses a problem that can happen when using an external YAML parser.
        // In JSON, the keys must always be strings, and System.Text.Json will refuse to either serialize or
        // deserialize anything with non-string keys. But in YAML, the keys can be of any type, so a YAML
        // parser that is told to deserialize some map-like data without a specific target type may decide to
        // return the type Dictionary<object, object> even if the keys really are strings.
        private class UntypedDictionaryJsonSerializer : JsonConverter<IDictionary<object, object>>
        {
            public override bool CanConvert(Type typeToConvert) =>
                typeof(IDictionary<object, object>).IsAssignableFrom(typeToConvert);

            public override IDictionary<object, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, IDictionary<object, object> value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                foreach (var kv in value)
                {
                    writer.WritePropertyName(kv.Key.ToString());
                    JsonSerializer.Serialize(writer, kv.Value, options);
                }
                writer.WriteEndObject();
            }
        }
    }
}
