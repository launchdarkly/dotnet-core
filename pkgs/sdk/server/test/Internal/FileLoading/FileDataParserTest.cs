using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;
using YamlDotNet.Serialization;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataParserTest
    {
        private const string JsonPath = "/data/flags.json";
        private const string YamlPath = "/data/flags.yaml";

        private const string FullDocument = @"{
            ""flags"": {
                ""flag1"": { ""key"": ""flag1"", ""version"": 7, ""on"": true, ""fallthrough"": { ""variation"": 1 },
                    ""variations"": [ ""a"", ""b"" ] }
            },
            ""flagValues"": { ""flag2"": ""value2"", ""flag3"": true },
            ""segments"": {
                ""seg1"": { ""key"": ""seg1"", ""version"": 3, ""included"": [ ""user1"" ] }
            }
        }";

        private static readonly FileDataParser JsonOnly = new FileDataParser(null);

        private static Func<string, object> YamlParser()
        {
            var yaml = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
            return s => yaml.Deserialize<object>(s);
        }

        private static FileDataParser WithParser(string extension, Func<string, object> parser) =>
            new FileDataParser(new Dictionary<string, Func<string, object>> { [extension] = parser });

        [Fact]
        public void ParsesFlagsFlagValuesAndSegmentsInOrder()
        {
            var doc = JsonOnly.Parse(JsonPath, FullDocument);

            Assert.Equal(new[] { "flag1" }, doc.Flags.Select(kv => kv.Key));
            Assert.Equal(7, doc.Flags[0].Value.Version);
            Assert.True(doc.Flags[0].Value.On);

            Assert.Equal(new[] { "flag2", "flag3" }, doc.FlagValues.Select(kv => kv.Key));
            Assert.Equal(LdValue.Of("value2"), doc.FlagValues[0].Value);
            Assert.Equal(LdValue.Of(true), doc.FlagValues[1].Value);

            Assert.Equal(new[] { "seg1" }, doc.Segments.Select(kv => kv.Key));
            Assert.Equal(3, doc.Segments[0].Value.Version);
            Assert.Equal(new[] { "user1" }, doc.Segments[0].Value.Included);
        }

        [Fact]
        public void KeepsDocumentVersions()
        {
            var doc = JsonOnly.Parse(JsonPath, FullDocument);
            Assert.Equal(7, doc.Flags[0].Value.Version);
            Assert.Equal(3, doc.Segments[0].Value.Version);
        }

        [Fact]
        public void VersionIsOptional()
        {
            var doc = JsonOnly.Parse(JsonPath,
                @"{""flags"": {""flag1"": {""on"": true}}, ""segments"": {""seg1"": {""included"": [""u""]}}}");
            Assert.Equal(0, doc.Flags[0].Value.Version);
            Assert.Equal(0, doc.Segments[0].Value.Version);
        }

        [Fact]
        public void EntryKeyIsTheKeyWhenTheDefinitionHasNone()
        {
            var doc = JsonOnly.Parse(JsonPath,
                @"{""flags"": {""flag1"": {""on"": true}}, ""segments"": {""seg1"": {""included"": [""u""]}}}");
            Assert.Equal("flag1", doc.Flags[0].Key);
            Assert.Equal("flag1", doc.Flags[0].Value.Key);
            Assert.Equal("seg1", doc.Segments[0].Key);
            Assert.Equal("seg1", doc.Segments[0].Value.Key);
        }

        [Fact]
        public void DefinitionWithADifferentKeyIsRejected()
        {
            var e = Assert.Throws<JsonException>(() =>
                JsonOnly.Parse(JsonPath, @"{""flags"": {""flag1"": {""key"": ""other"", ""on"": true}}}"));
            Assert.Contains("flag \"flag1\" has a different key property \"other\"", e.Message);

            e = Assert.Throws<JsonException>(() =>
                JsonOnly.Parse(JsonPath, @"{""segments"": {""seg1"": {""key"": ""other""}}}"));
            Assert.Contains("segment \"seg1\" has a different key property \"other\"", e.Message);
        }

        [Fact]
        public void DeletedDefinitionIsRejected()
        {
            var e = Assert.Throws<JsonException>(() =>
                JsonOnly.Parse(JsonPath, @"{""flags"": {""flag1"": {""deleted"": true}}}"));
            Assert.Contains("flag \"flag1\" is marked as deleted", e.Message);

            e = Assert.Throws<JsonException>(() =>
                JsonOnly.Parse(JsonPath, @"{""segments"": {""seg1"": {""version"": 2, ""deleted"": true}}}"));
            Assert.Contains("segment \"seg1\" is marked as deleted", e.Message);
        }

        [Fact]
        public void EmptyObjectIsAnEmptyDocument()
        {
            var doc = JsonOnly.Parse(JsonPath, "{}");
            Assert.Empty(doc.Flags);
            Assert.Empty(doc.FlagValues);
            Assert.Empty(doc.Segments);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" \n\t")]
        public void ContentWithNoDocumentIsAnEmptyDocument(string content)
        {
            var doc = JsonOnly.Parse(JsonPath, content);
            Assert.Empty(doc.Flags);
            Assert.Empty(doc.FlagValues);
            Assert.Empty(doc.Segments);
        }

        [Fact]
        public void NullSectionsAreEmpty()
        {
            var doc = JsonOnly.Parse(JsonPath, @"{""flags"": null, ""flagValues"": null, ""segments"": null}");
            Assert.Empty(doc.Flags);
            Assert.Empty(doc.FlagValues);
            Assert.Empty(doc.Segments);
        }

        [Fact]
        public void UnknownTopLevelPropertiesAreIgnored()
        {
            var doc = JsonOnly.Parse(JsonPath, @"{""other"": {""x"": 1}, ""flagValues"": {""flag1"": 1}}");
            Assert.Single(doc.FlagValues);
        }

        [Theory]
        [InlineData("what is this")]
        [InlineData(@"{""flagValues""")]
        [InlineData(@"{""flagValues"": {""flag1"": }}")]
        [InlineData("[]")]
        [InlineData("null")]
        public void MalformedContentThrows(string content)
        {
            Assert.ThrowsAny<Exception>(() => JsonOnly.Parse(JsonPath, content));
        }

        [Theory]
        [InlineData(@"{""flagValues"": {""a"": 9}}""b"": 2, ""c"": 3}}")]
        [InlineData(@"{""flagValues"": {""a"": 9}} {}")]
        [InlineData(@"{""flagValues"": {""a"": 9}} x")]
        public void ContentAfterTheDocumentIsRejected(string content)
        {
            Assert.ThrowsAny<JsonException>(() => JsonOnly.Parse(JsonPath, content));
        }

        [Fact]
        public void ContentIsParsedAsJsonWhenTheExtensionHasNoParser()
        {
            var parser = WithParser(".yaml", s => throw new Exception("the YAML parser must not be called"));
            Assert.Single(parser.Parse(JsonPath, FullDocument).Flags);
            Assert.Single(parser.Parse("/data/flags", FullDocument).Flags);
            Assert.Single(parser.Parse("/data/flags.txt", FullDocument).Flags);
            Assert.Single(parser.Parse("/data/flags.JSON", FullDocument).Flags);
        }

        [Fact]
        public void RegisteredParserIsUsedForItsExtension()
        {
            var parser = WithParser(".yaml", YamlParser());

            var doc = parser.Parse(YamlPath,
                "flagValues:\n  flag1: true\n  flag2: \"text\"\nsegments:\n  seg1:\n    key: seg1\n    version: 2\n");

            Assert.Equal(new[] { "flag1", "flag2" }, doc.FlagValues.Select(kv => kv.Key));
            Assert.Equal(LdValue.Of(true), doc.FlagValues[0].Value);
            Assert.Equal(LdValue.Of("text"), doc.FlagValues[1].Value);
            Assert.Equal(2, doc.Segments[0].Value.Version);
        }

        [Theory]
        [InlineData("yaml", "/data/flags.yaml")]
        [InlineData(".YAML", "/data/flags.yaml")]
        [InlineData(".yaml", "/data/flags.YAML")]
        [InlineData("Yml", "/data/flags.yml")]
        public void ExtensionsMatchWithoutRegardToThePeriodOrLetterCase(string registered, string path)
        {
            var parser = WithParser(registered, YamlParser());
            var doc = parser.Parse(path, "flagValues:\n  flag1: true\n");
            Assert.Single(doc.FlagValues);
        }

        [Fact]
        public void RegisteredParserGetsTheContentEvenWhenItLooksLikeJson()
        {
            var seen = new List<string>();
            var parser = WithParser(".yaml", s =>
            {
                seen.Add(s);
                return YamlParser()(s);
            });
            parser.Parse(YamlPath, FullDocument);
            Assert.Equal(new[] { FullDocument }, seen);
        }

        [Theory]
        [InlineData("")]
        [InlineData("# a comment only\n")]
        public void RegisteredParserResultOfNullIsAnEmptyDocument(string content)
        {
            var parser = WithParser(".yaml", YamlParser());
            var doc = parser.Parse(YamlPath, content);
            Assert.Empty(doc.Flags);
            Assert.Empty(doc.FlagValues);
            Assert.Empty(doc.Segments);
        }

        [Fact]
        public void RegisteredParserFailureIsThrown()
        {
            var parser = WithParser(".yaml", s => throw new FormatException("bad yaml"));
            Assert.Throws<FormatException>(() => parser.Parse(YamlPath, "not yaml"));
        }

        [Fact]
        public void RegisteredParserResultThatIsNotAnObjectIsRejected()
        {
            var parser = WithParser(".yaml", s => "just a string");
            Assert.Throws<JsonException>(() => parser.Parse(YamlPath, "just a string"));
        }

        [Theory]
        [InlineData("json", ".json")]
        [InlineData(".json", ".json")]
        [InlineData(".YAML", ".yaml")]
        [InlineData("Yml", ".yml")]
        public void NormalizeExtensionAddsThePeriodAndLowersTheCase(string given, string expected)
        {
            Assert.Equal(expected, FileDataParser.NormalizeExtension(given));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(".")]
        public void NormalizeExtensionRejectsAnEmptyExtension(string given)
        {
            Assert.Throws<ArgumentException>(() => FileDataParser.NormalizeExtension(given));
        }

        [Fact]
        public void FindsAYamlPathWithoutAParser()
        {
            var yamlOnly = new Dictionary<string, Func<string, object>> { [".yaml"] = s => null };
            var paths = new[] { "/data/a.json", "/data/b.yml", "/data/c.yaml" };

            Assert.Equal("/data/b.yml", FileDataParser.FindYamlPathWithoutParser(paths, null));
            Assert.Equal("/data/b.yml", FileDataParser.FindYamlPathWithoutParser(paths, yamlOnly));
            Assert.Equal("/data/c.YAML",
                FileDataParser.FindYamlPathWithoutParser(new[] { "/data/a.json", "/data/c.YAML" }, null));
            Assert.Null(FileDataParser.FindYamlPathWithoutParser(new[] { "/data/a.json", "/data/b", "/data/c.txt" }, null));
            Assert.Null(FileDataParser.FindYamlPathWithoutParser(new[] { "/data/c.yaml" }, yamlOnly));
        }

        [Fact]
        public void FallthroughFlagWithValueServesTheValueWithTheFallthroughReason()
        {
            var flag = FileDataParser.MakeFallthroughFlagWithValue("flag1", LdValue.Of("x"), 0);
            Assert.Equal("flag1", flag.Key);
            Assert.Equal(0, flag.Version);
            Assert.True(flag.On);
            Assert.Null(flag.OffVariation);
            Assert.Equal(new[] { LdValue.Of("x") }, flag.Variations);

            var result = Evaluation.EvaluatorTestUtil.BasicEvaluator.Evaluate(flag, Context.New("any-user"));
            Assert.Equal(LdValue.Of("x"), result.Result.Value);
            Assert.Equal(0, result.Result.VariationIndex);
            Assert.Equal(EvaluationReason.FallthroughReason, result.Result.Reason);
        }
    }
}
