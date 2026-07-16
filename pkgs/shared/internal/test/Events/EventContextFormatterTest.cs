using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class EventContextFormatterTest
    {
        private struct Params
        {
            public string name;
            public Context context;
            public EventsConfiguration config;
            public string json;
        }

        private static List<Params> TestCases = new List<Params>
        {
            new Params
            {
                name = "no attributes private, single kind",
                context = Context.Builder("my-key").Kind("org").
                    Name("my-name").
                    Set("attr1", "value1").
                    Build(),
                json = @"{""kind"": ""org"", ""key"": ""my-key"", ""name"": ""my-name"", ""attr1"": ""value1""}"
            },
            new Params
            {
                name = "no attributes private, multi-kind",
                context = Context.NewMulti(
                    Context.Builder("org-key").Kind("org").
                        Name("org-name").
                        Build(),
                    Context.Builder("user-key").
                        Name("user-name").
                        Set("attr1", "value1").
                        Build()
                    ),
                json = @"{
                    ""kind"": ""multi"",
                    ""org"": {""key"": ""org-key"", ""name"": ""org-name""},
                    ""user"": {""key"": ""user-key"", ""name"": ""user-name"", ""attr1"": ""value1""}
                }"
            },
            new Params
            {
                name = "anonymous",
                context = Context.Builder("my-key").Kind("org").Anonymous(true).Build(),
                json = @"{""kind"": ""org"", ""key"": ""my-key"", ""anonymous"": true}"
            },
            new Params
            {
                name = "all attributes private globally",
                context = Context.Builder("my-key").Kind("org").
                    Name("my-name").
                    Set("attr1", "value1").
                    Build(),
                config = new EventsConfiguration { AllAttributesPrivate = true },
                json = @"{
                    ""kind"": ""org"",
                    ""key"": ""my-key"",
                    ""_meta"": {
                        ""redactedAttributes"": [""attr1"", ""name""]
                    }
		        }"
            },
            new Params
            {
                name = "some top-level attributes private",
                context = Context.Builder("my-key").Kind("org").
                    Name("my-name").
                    Set("attr1", "value1").
                    Set("attr2", "value2").
                    Private("attr2").
                    Build(),
                config = new EventsConfiguration {
                    PrivateAttributes = ImmutableHashSet.Create(
                        AttributeRef.FromLiteral("name")
                        )
                },
                json = @"{
                    ""kind"": ""org"",
                    ""key"": ""my-key"",
                    ""attr1"": ""value1"",
                    ""_meta"": {
                        ""redactedAttributes"": [""attr2"", ""name""]
                    }
		        }"
            },
            new Params
            {
                name = "partially redacting object attributes",
                context = Context.Builder("my-key").
                    Set("address", LdValue.Parse(@"{""street"": ""17 Highbrow St."", ""city"": ""London""}")).
                    Set("complex", LdValue.Parse(@"{""a"": {""b"": {""c"": 1, ""d"": 2}, ""e"": 3}, ""f"": 4, ""g"": 5}")).
                    Private("/complex/a/b/d", "/complex/a/b/nonexistent-prop", "/complex/f", "/complex/g/g-is-not-an-object").
                    Build(),
                config = new EventsConfiguration {
                    PrivateAttributes = ImmutableHashSet.Create(
                        AttributeRef.FromPath("/address/street")
                        )
                },
                json = @"{
                    ""kind"": ""user"",
                    ""key"": ""my-key"",
                    ""address"": {""city"": ""London""},
                    ""complex"": {""a"": {""b"": {""c"": 1}, ""e"": 3}, ""g"": 5},
                    ""_meta"": {
                        ""redactedAttributes"": [""/address/street"", ""/complex/a/b/d"", ""/complex/f""]
                    }
		        }"
            },
        };

        public static IEnumerable<object[]> TestCaseNames => TestCases.Select(p => new object[] { p.name });

        [Theory]
        [MemberData(nameof(TestCaseNames))]
        public void TestOutput(string testCaseName)
        {
            // This somewhat indirect way of doing a parameterized test is necessary because Xunit has trouble
            // dealing with complex types as test parameters.
            var p = TestCases.Find(c => c.name == testCaseName);

            var stream = new MemoryStream();
            var w = new Utf8JsonWriter(stream);
            new EventContextFormatter(p.config ?? new EventsConfiguration()).Write(p.context, w);
            w.Flush();
            var json = Encoding.UTF8.GetString(stream.ToArray());

            LdValue parsedJson = TestUtil.TryParseJson(json);
            AssertJsonEqual(p.json, ValueWithRedactedAttributesSorted(parsedJson).ToJsonString());
        }

        [Fact]
        public void TestSingleKindAnonymousContextsAreRedactedAppropriately()
        {
            var context = Context.Builder("my-key").Kind("org").
                Anonymous(true).
                Name("my-name").
                Set("attr1", "value1").
                Build();

            var stream = new MemoryStream();
            var w = new Utf8JsonWriter(stream);
            new EventContextFormatter(new EventsConfiguration()).Write(context, w, redactAnonymous: true);
            w.Flush();
            var json = Encoding.UTF8.GetString(stream.ToArray());

            var expectedJson = @"{""kind"": ""org"", ""key"": ""my-key"", ""anonymous"": true, ""_meta"": { ""redactedAttributes"": [""attr1"", ""name""]}}";

            LdValue parsedJson = TestUtil.TryParseJson(json);
            AssertJsonEqual(expectedJson, ValueWithRedactedAttributesSorted(parsedJson).ToJsonString());
        }

        [Fact]
        public void TestMultiKindAnonymousContextsAreRedactedAppropriately()
        {
            var userContext = Context.Builder("user-key").Kind("user").
                Anonymous(true).
                Name("Example user name").
                Set("attr1", "value1").
                Build();
            var orgContext = Context.Builder("org-key").Kind("org").
                Anonymous(false).
                Name("Example org name").
                Set("attr1", "value1").
                Build();
            var multi = Context.NewMulti(userContext, orgContext);

            var stream = new MemoryStream();
            var w = new Utf8JsonWriter(stream);
            new EventContextFormatter(new EventsConfiguration()).Write(multi, w, redactAnonymous: true);
            w.Flush();
            var json = Encoding.UTF8.GetString(stream.ToArray());

            var expectedJson = @"{""kind"": ""multi"", ""org"": {""key"": ""org-key"", ""name"": ""Example org name"", ""attr1"": ""value1""}, ""user"": {""key"": ""user-key"", ""anonymous"": true, ""_meta"": { ""redactedAttributes"": [""attr1"", ""name""]}}}";

            LdValue parsedJson = TestUtil.TryParseJson(json);
            AssertJsonEqual(expectedJson, ValueWithRedactedAttributesSorted(parsedJson).ToJsonString());
        }

        private static string JsonWithRedactedAttributesSorted(string input) =>
            ValueWithRedactedAttributesSorted(LdValue.Parse(input)).ToJsonString();

        private static LdValue ValueWithRedactedAttributesSorted(LdValue value)
        {
            switch (value.Type)
            {
                case LdValueType.Array:
                    return LdValue.ArrayFrom(value.List.Select(ValueWithRedactedAttributesSorted));
                case LdValueType.Object:
                    return LdValue.ObjectFrom(value.Dictionary.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Key == "redactedAttributes" ?
                            LdValue.Convert.String.ArrayFrom(kv.Value.AsList(LdValue.Convert.String).OrderBy(s => s))
                            : ValueWithRedactedAttributesSorted(kv.Value)));
                default:
                    return value;
            }
        }
    }
}
