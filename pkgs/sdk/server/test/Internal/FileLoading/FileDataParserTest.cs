using System;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Internal.Model;
using Xunit;
using YamlDotNet.Serialization;

namespace LaunchDarkly.Sdk.Server.Internal.FileLoading
{
    public class FileDataParserTest
    {
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

        [Fact]
        public void ParsesFlagsFlagValuesAndSegmentsInOrder()
        {
            var doc = JsonOnly.Parse(FullDocument);

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
            var doc = JsonOnly.Parse(FullDocument);
            Assert.Equal(7, doc.Flags[0].Value.Version);
            Assert.Equal(3, doc.Segments[0].Value.Version);
        }

        [Fact]
        public void EmptyObjectIsAnEmptyDocument()
        {
            var doc = JsonOnly.Parse("{}");
            Assert.Empty(doc.Flags);
            Assert.Empty(doc.FlagValues);
            Assert.Empty(doc.Segments);
        }

        [Fact]
        public void NullSectionsAreEmpty()
        {
            var doc = JsonOnly.Parse(@"{""flags"": null, ""flagValues"": null, ""segments"": null}");
            Assert.Empty(doc.Flags);
            Assert.Empty(doc.FlagValues);
            Assert.Empty(doc.Segments);
        }

        [Fact]
        public void UnknownTopLevelPropertiesAreIgnored()
        {
            var doc = JsonOnly.Parse(@"{""other"": {""x"": 1}, ""flagValues"": {""flag1"": 1}}");
            Assert.Single(doc.FlagValues);
        }

        [Theory]
        [InlineData("")]
        [InlineData("what is this")]
        [InlineData(@"{""flagValues""")]
        [InlineData(@"{""flagValues"": {""flag1"": }}")]
        [InlineData("[]")]
        public void MalformedContentThrowsWithoutAlternateParser(string content)
        {
            Assert.ThrowsAny<Exception>(() => JsonOnly.Parse(content));
        }

        [Fact]
        public void AlternateParserIsUsedForNonJsonContent()
        {
            var yaml = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
            var parser = new FileDataParser(s => yaml.Deserialize<object>(s));

            var doc = parser.Parse("flagValues:\n  flag1: true\n  flag2: \"text\"\nsegments:\n  seg1:\n    key: seg1\n    version: 2\n");

            Assert.Equal(new[] { "flag1", "flag2" }, doc.FlagValues.Select(kv => kv.Key));
            Assert.Equal(LdValue.Of(true), doc.FlagValues[0].Value);
            Assert.Equal(LdValue.Of("text"), doc.FlagValues[1].Value);
            Assert.Equal(2, doc.Segments[0].Value.Version);
        }

        [Fact]
        public void JsonIsParsedAsJsonWhenAlternateParserIsConfigured()
        {
            var parser = new FileDataParser(s => throw new Exception("alternate parser must not be called"));
            var doc = parser.Parse(FullDocument);
            Assert.Single(doc.Flags);
        }

        [Fact]
        public void AlternateParserGetsContentThatStartsLikeJsonButIsNot()
        {
            // YAML flow mappings start with a brace but are not JSON. Both parsers get a chance.
            var yaml = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
            var parser = new FileDataParser(s => yaml.Deserialize<object>(s));
            var doc = parser.Parse("{flagValues: {flag1: yes}}");
            Assert.Single(doc.FlagValues);
        }

        [Fact]
        public void AlternateParserFailureIsThrown()
        {
            var parser = new FileDataParser(s => throw new FormatException("bad yaml"));
            Assert.Throws<FormatException>(() => parser.Parse("not json"));
        }

        [Fact]
        public void FlagWithVersionReplacesOnlyTheVersion()
        {
            var flag = new FeatureFlagBuilder("flag1").Version(1).On(true).Variations(LdValue.Of("a")).Build();
            var reversioned = FileDataParser.FlagWithVersion(flag, 5);
            Assert.Equal(5, reversioned.Version);
            Assert.Equal("flag1", reversioned.Key);
            Assert.True(reversioned.On);
            Assert.Equal(flag.Variations, reversioned.Variations);
            Assert.Same(flag, FileDataParser.FlagWithVersion(flag, 1));
        }

        [Fact]
        public void SegmentWithVersionReplacesOnlyTheVersion()
        {
            var segment = new SegmentBuilder("seg1").Version(1).Included("user1").Build();
            var reversioned = FileDataParser.SegmentWithVersion(segment, 5);
            Assert.Equal(5, reversioned.Version);
            Assert.Equal(new[] { "user1" }, reversioned.Included);
            Assert.Same(segment, FileDataParser.SegmentWithVersion(segment, 1));
        }

        [Fact]
        public void OffFlagWithValueServesTheValueWithTheOffReason()
        {
            var flag = FileDataParser.MakeOffFlagWithValue("flag1", LdValue.Of("x"), 0);
            Assert.Equal("flag1", flag.Key);
            Assert.Equal(0, flag.Version);
            Assert.False(flag.On);
            Assert.Equal(0, flag.OffVariation);
            Assert.Equal(new[] { LdValue.Of("x") }, flag.Variations);

            var result = Evaluation.EvaluatorTestUtil.BasicEvaluator.Evaluate(flag, Context.New("any-user"));
            Assert.Equal(LdValue.Of("x"), result.Result.Value);
            Assert.Equal(0, result.Result.VariationIndex);
            Assert.Equal(EvaluationReason.OffReason, result.Result.Reason);
        }

        [Fact]
        public void FallthroughFlagWithValueServesTheValueForEveryContext()
        {
            var flag = FileDataParser.MakeFallthroughFlagWithValue("flag1", LdValue.Of("x"), 3);
            Assert.Equal("flag1", flag.Key);
            Assert.Equal(3, flag.Version);
            Assert.True(flag.On);
            Assert.Equal(new[] { LdValue.Of("x") }, flag.Variations);
            Assert.Equal(0, flag.Fallthrough.Variation);

            var result = Evaluation.EvaluatorTestUtil.BasicEvaluator.Evaluate(flag, Context.New("any-user"));
            Assert.Equal(LdValue.Of("x"), result.Result.Value);
            Assert.Equal(EvaluationReason.FallthroughReason, result.Result.Reason);
        }
    }
}
