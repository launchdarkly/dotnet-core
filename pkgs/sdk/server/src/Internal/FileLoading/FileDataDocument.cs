using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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
    /// Parses the file data document format: an object with optional <c>flags</c>, <c>flagValues</c>,
    /// and <c>segments</c> members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parser for a file is chosen by the file's extension. An extension with a registered parser
    /// uses that parser; every other file is parsed as JSON. The registered parsers are for formats
    /// such as YAML, which the SDK does not parse itself.
    /// </para>
    /// <para>
    /// A flag or segment is listed under its key. A <c>key</c> property in the definition is optional
    /// and must be equal to the key it is listed under; a <c>version</c> property is optional. A
    /// definition that is marked as deleted is rejected: a data file holds definitions only. A file
    /// with no content is a document with no entries, and content after the end of the document is
    /// an error.
    /// </para>
    /// </remarks>
    internal sealed class FileDataParser
    {
        /// <summary>
        /// The extensions of YAML files. A path with one of these needs a registered parser, because
        /// the SDK does not include a YAML parser. The check belongs to configuration time, so that a
        /// file that can never load is reported when the client is created rather than on every load.
        /// </summary>
        internal static readonly IReadOnlyList<string> YamlExtensions = ImmutableList.Create(".yaml", ".yml");

        private static readonly IReadOnlyDictionary<string, Func<string, object>> NoParsers =
            ImmutableDictionary<string, Func<string, object>>.Empty;

        private readonly IReadOnlyDictionary<string, Func<string, object>> _parsersByExtension;

        /// <summary>
        /// Constructs a parser.
        /// </summary>
        /// <param name="parsersByExtension">parsers for formats other than JSON, by file extension; the
        /// extensions are normalized with <see cref="NormalizeExtension"/>; null means JSON only</param>
        internal FileDataParser(IReadOnlyDictionary<string, Func<string, object>> parsersByExtension)
        {
            if (parsersByExtension is null || parsersByExtension.Count == 0)
            {
                _parsersByExtension = NoParsers;
                return;
            }
            var normalized = new Dictionary<string, Func<string, object>>();
            foreach (var kv in parsersByExtension)
            {
                normalized[NormalizeExtension(kv.Key)] = kv.Value;
            }
            _parsersByExtension = normalized;
        }

        /// <summary>
        /// Normalizes a file extension as given in configuration: a leading period is added when it is
        /// missing, and letter case is ignored.
        /// </summary>
        /// <param name="extension">the extension, with or without a leading period</param>
        /// <returns>the normalized extension</returns>
        /// <exception cref="ArgumentException">the extension is null or has no characters other than a period</exception>
        internal static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension) || extension == ".")
            {
                throw new ArgumentException("file extension must not be empty", nameof(extension));
            }
            var withPeriod = extension[0] == '.' ? extension : "." + extension;
            return withPeriod.ToLowerInvariant();
        }

        /// <summary>
        /// The normalized extension of a path: the part of the file name from its last period, in
        /// lower case, or the empty string when the file name has no period.
        /// </summary>
        internal static string ExtensionOf(string path) => Path.GetExtension(path).ToLowerInvariant();

        /// <summary>
        /// Finds a configured path that is a YAML file by its extension but has no registered parser.
        /// Such a file can never load, so a configuration that lists one is an error.
        /// </summary>
        /// <param name="paths">the configured paths</param>
        /// <param name="parsersByExtension">the registered parsers, by normalized extension</param>
        /// <returns>the first such path, or null when there is none</returns>
        internal static string FindYamlPathWithoutParser(IEnumerable<string> paths,
            IReadOnlyDictionary<string, Func<string, object>> parsersByExtension)
        {
            foreach (var path in paths)
            {
                var extension = ExtensionOf(path);
                if (YamlExtensions.Contains(extension) &&
                    (parsersByExtension is null || !parsersByExtension.ContainsKey(extension)))
                {
                    return path;
                }
            }
            return null;
        }

        /// <summary>
        /// Parses the content of one file. Throws an exception if the content cannot be parsed.
        /// </summary>
        /// <param name="path">the file's path, which selects the parser by its extension</param>
        /// <param name="content">the file content</param>
        /// <returns>the parsed document</returns>
        internal FileDataDocument Parse(string path, string content)
        {
            if (!_parsersByExtension.TryGetValue(ExtensionOf(path), out var parser))
            {
                return ParseJson(content);
            }
            // The registered parser produces the most basic .NET data structure that can represent
            // the file content, using types like Dictionary and String. We convert this into a JSON
            // tree so we can use the JSON deserializer. This is inefficient, but it lets us reuse the
            // data model deserialization logic.
            var o = parser(content);
            if (o is null)
            {
                // A file with no content, or with comments only, parses to null in formats such as
                // YAML. It is a document with no entries.
                return FileDataDocument.Empty;
            }
            var options = new JsonSerializerOptions();
            options.Converters.Add(new UntypedDictionaryJsonSerializer());
            return ParseJson(JsonSerializer.Serialize(o, options));
        }

        private static FileDataDocument ParseJson(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                // A file with no content is a document with no entries.
                return FileDataDocument.Empty;
            }
            var r = new Utf8JsonReader(Encoding.UTF8.GetBytes(data));
            var document = ParseJson(ref r);
            // Content after the document means the file is not one complete document, for example
            // a file that was rewritten in place to a shorter length. Reading past the end makes the
            // reader check for that; it rejects anything other than white space.
            if (r.Read())
            {
                throw new JsonException("unexpected content after the end of the document");
            }
            return document;
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
                            var flag = FeatureFlagSerialization.Instance.ReadFileEntry(ref r, key);
                            RejectDeleted("flag", key, flag.Deleted);
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
                            var segment = SegmentSerialization.Instance.ReadFileEntry(ref r, key);
                            RejectDeleted("segment", key, segment.Deleted);
                            segmentsBuilder.Add(new KeyValuePair<string, Segment>(key, segment));
                        }
                        break;
                }
            }
            return new FileDataDocument(flagsBuilder.ToImmutable(), flagValuesBuilder.ToImmutable(),
                segmentsBuilder.ToImmutable());
        }

        // A deleted entry is a placeholder for data that was removed. A data file lists the data
        // that exists, so a definition marked as deleted is a mistake, not a removal.
        private static void RejectDeleted(string category, string key, bool deleted)
        {
            if (deleted)
            {
                throw new JsonException(category + " \"" + key + "\" is marked as deleted; remove the entry instead");
            }
        }

        /// <summary>
        /// Constructs a flag that is on and serves the same value for every context. The flag has a
        /// single variation and serves it as its fallthrough, so it evaluates with the
        /// <see cref="EvaluationReasonKind.Fallthrough"/> reason. This is the form the override source
        /// uses for <c>flagValues</c> entries.
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
