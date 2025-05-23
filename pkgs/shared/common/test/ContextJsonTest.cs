using System;
using System.Text.Json;
using LaunchDarkly.Sdk.Json;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class ContextJsonTest
    {
        [Fact]
        public void ValidDataSerializationAndDeserializationTests()
        {
            // Can't use a parameterized test for this because Xunit can't handle Context as a parameter type:
            // https://stackoverflow.com/questions/30574322/memberdata-tests-show-up-as-one-test-instead-of-many

            TestBoth(Context.New(ContextKind.Of("org"), "key1"), @"{""kind"": ""org"", ""key"": ""key1""}");
            TestBoth(Context.New("key1b"), @"{""kind"": ""user"", ""key"": ""key1b""}");
            TestBoth(Context.Builder("key1c").Kind("org").Build(),
                @"{""kind"": ""org"", ""key"": ""key1c""}");
            TestBoth(Context.Builder("key2").Name("my-name").Build(),
                @"{""kind"": ""user"", ""key"": ""key2"", ""name"": ""my-name""}");
            TestBoth(Context.Builder("key4").Anonymous(true).Build(),
                @"{""kind"": ""user"", ""key"": ""key4"", ""anonymous"": true}");
            TestBoth(Context.Builder("key5").Anonymous(false).Build(),
                @"{""kind"": ""user"", ""key"": ""key5""}");
            TestBoth(Context.Builder("key6").Set("attr1", true).Build(),
                @"{""kind"": ""user"", ""key"": ""key6"", ""attr1"": true}");
            TestBoth(Context.Builder("key6").Set("attr1", false).Build(),
                @"{""kind"": ""user"", ""key"": ""key6"", ""attr1"": false}");
            TestBoth(Context.Builder("key6").Set("attr1", 123).Build(),
                @"{""kind"": ""user"", ""key"": ""key6"", ""attr1"": 123}");
            TestBoth(Context.Builder("key6").Set("attr1", 1.5).Build(),
                @"{""kind"": ""user"", ""key"": ""key6"", ""attr1"": 1.5}");
            TestBoth(Context.Builder("key6").Set("attr1", "xyz").Build(),
                @"{""kind"": ""user"", ""key"": ""key6"", ""attr1"": ""xyz""}");
            TestBoth(Context.Builder("key6").Set("attr1", LdValue.ArrayOf(LdValue.Of(10), LdValue.Of(20))).Build(),
                @"{""kind"": ""user"", ""key"": ""key6"", ""attr1"": [10, 20]}");
            TestBoth(Context.Builder("key6").Set("attr1", LdValue.BuildObject().Set("a", 1).Build()).Build(),
                @"{""kind"": ""user"", ""key"": ""key6"", ""attr1"": {""a"": 1}}");
            TestBoth(Context.Builder("key7").Private("a").Private(AttributeRef.FromPath("/b/c")).Build(),
                @"{""kind"": ""user"", ""key"": ""key7"", ""_meta"": {""privateAttributes"": [""a"", ""/b/c""]}}");
            TestBoth(Context.NewMulti(Context.New(ContextKind.Of("org"), "my-org-key"), Context.New("my-user-key")),
                @"{""kind"": ""multi"", ""org"": {""key"": ""my-org-key""}, ""user"": {""key"": ""my-user-key""}}");
        }

        [Fact]
        public void SerializeInvalidContext()
        {
            Assert.ThrowsAny<Exception>(() => LdJsonSerialization.SerializeObject(new Context()));

            Assert.ThrowsAny<Exception>(() => LdJsonSerialization.SerializeObject(Context.New("")));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("false")]
        [InlineData("1")]
        [InlineData(@"""x""")]
        [InlineData("[]")]
        [InlineData("{}")]
            // wrong type for top-level property:
        [InlineData(@"{""kind"": null, ""key"": ""a""}")]
        [InlineData(@"{""kind"": true, ""key"": ""a""}")]
        [InlineData(@"{""kind"": ""org"", ""key"": null}")]
        [InlineData(@"{""kind"": ""org"", ""key"": true}")]
        [InlineData(@"{""kind"": ""multi"", ""org"": null}")]
        [InlineData(@"{""kind"": ""multi"", ""org"": true}")]
        [InlineData(@"{""kind"": ""org"", ""key"": ""a"", ""name"": true}")]
        [InlineData(@"{""kind"": ""org"", ""key"": ""a"", ""anonymous"": ""yes""}")]
        [InlineData(@"{""kind"": ""org"", ""key"": ""a"", ""anonymous"": null}")]
            // invalid kind/key
        [InlineData(@"{""kind"": ""org""}")]
        [InlineData(@"{""kind"": ""user"", ""key"": """"}")]
        [InlineData(@"{""kind"": """", ""key"": ""x""}")]
        [InlineData(@"{""kind"": ""ørg"", ""key"": ""x""}")]
            // wrong type within _meta
        [InlineData(@"{""kind"": ""org"", ""key"": ""my-key"", ""_meta"": true}")]
        [InlineData(@"{""kind"": ""org"", ""key"": ""my-key"", ""_meta"": {""privateAttributes"": true}}")]
            // multi-kind problems
        [InlineData(@"{""kind"": ""multi""}")]
        [InlineData(@"{""kind"": ""multi"", ""user"": {""key"": """"}}")]
        [InlineData(@"{""kind"": ""multi"", ""user"": {""key"": true}}")]
            // wrong types in old user schema
        [InlineData(@"{""key"": null}")]
        [InlineData(@"{""key"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""anonymous"": ""x""}")]
        [InlineData(@"{""key"": ""my-key"", ""name"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""firstName"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""lastName"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""email"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""country"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""avatar"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""ip"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""custom"": true}")]
        [InlineData(@"{""key"": ""my-key"", ""privateAttributeNames"": true}")]

        //// missing key in old user schema
        //`{ "name": "x"}`,
        public void DeserializeInvalidContext(string input)
        {
            LdJsonSerialization.DeserializeObject<LdValue>(input); // just to be sure it's valid JSON
            Assert.ThrowsAny<JsonException>(() => LdJsonSerialization.DeserializeObject<Context>(input));
        }

        [Fact]
        public void DeserializeOldUserSchema()
        {
            TestDeserializeOnly(Context.New("key1"), @"{""key"": ""key1""}");
            TestDeserializeOnly(Context.Builder("key2").Name("my-name").Build(),
                @"{""key"": ""key2"", ""name"": ""my-name""}");
            TestDeserializeOnly(Context.Builder("key2").Set("firstName", "a").Build(),
                @"{""key"": ""key2"", ""firstName"": ""a""}");
            TestDeserializeOnly(Context.Builder("key2").Set("lastName", "a").Build(),
                @"{""key"": ""key2"", ""lastName"": ""a""}");
            TestDeserializeOnly(Context.Builder("key2").Set("email", "a").Build(),
                @"{""key"": ""key2"", ""email"": ""a""}");
            TestDeserializeOnly(Context.Builder("key2").Set("country", "a").Build(),
                @"{""key"": ""key2"", ""country"": ""a""}");
            TestDeserializeOnly(Context.Builder("key2").Set("ip", "a").Build(),
                @"{""key"": ""key2"", ""ip"": ""a""}");
            TestDeserializeOnly(Context.Builder("key2").Set("avatar", "a").Build(),
                @"{""key"": ""key2"", ""avatar"": ""a""}");
            TestDeserializeOnly(Context.Builder("key4").Anonymous(true).Build(),
                @"{""key"": ""key4"", ""anonymous"": true}");
            TestDeserializeOnly(Context.Builder("key5").Anonymous(false).Build(),
                @"{""key"": ""key5"", ""anonymous"": false}");
            TestDeserializeOnly(Context.Builder("key6").Set("attr1", true).Build(),
                @"{""key"": ""key6"", ""custom"": {""attr1"": true}}");
            TestDeserializeOnly(Context.Builder("key6").Set("attr1", false).Build(),
                @"{""key"": ""key6"", ""custom"": {""attr1"": false}}");
            TestDeserializeOnly(Context.Builder("key6").Set("attr1", 123).Build(),
                @"{""key"": ""key6"", ""custom"": {""attr1"": 123}}");
            TestDeserializeOnly(Context.Builder("key6").Set("attr1", 1.5).Build(),
                @"{""key"": ""key6"", ""custom"": {""attr1"": 1.5}}");
            TestDeserializeOnly(Context.Builder("key6").Set("attr1", "xyz").Build(),
                @"{""key"": ""key6"", ""custom"": {""attr1"": ""xyz""}}");
            TestDeserializeOnly(Context.Builder("key6").Set("attr1", LdValue.ArrayOf(LdValue.Of(10), LdValue.Of(20))).Build(),
                @"{""key"": ""key6"", ""custom"": {""attr1"": [10, 20]}}");
            TestDeserializeOnly(Context.Builder("key6").Set("attr1", LdValue.BuildObject().Set("a", 1).Build()).Build(),
                @"{""key"": ""key6"", ""custom"": {""attr1"": {""a"": 1}}}");
            TestDeserializeOnly(Context.Builder("key7").Private("a").Build(),
                @"{""key"": ""key7"", ""privateAttributeNames"": [""a""]}");
        }

        [Fact]
        public void EmptyKeyIsAllowedInOldUserSchema()
        {
            var c = LdJsonSerialization.DeserializeObject<Context>(@"{""key"": """"}");
            Assert.Equal(ContextKind.Default, c.Kind);
            Assert.Equal("", c.Key);
        }

        private static void TestBoth(Context c, string expectedJson)
        {
            TestSerializeOnly(c, expectedJson);
            TestDeserializeOnly(c, expectedJson);
        }

        private static void TestSerializeOnly(Context c, string expectedJson) =>
            JsonAssertions.AssertJsonEqual(expectedJson, LdJsonSerialization.SerializeObject(c));

        private static void TestDeserializeOnly(Context c, string expectedJson) =>
            Assert.Equal(c, LdJsonSerialization.DeserializeObject<Context>(expectedJson));
    }
}
