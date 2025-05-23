using System.Collections.Generic;
using LaunchDarkly.Sdk.Json;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class UserSerializationTests : UserBuilderTestBase
    {
        private const string key = "UserKey";

        // We test user deserialization just because a developer who sees Newtonsoft.Json attributes used in
        // the User class could reasonably expect both serialization and deserialization to work correctly.
        // We never deserialize users in the .NET or Xamarin SDK.

        [Fact]
        public void DeserializeBasicUserAsJson()
        {
            var json = $"{{\"key\":\"{key}\"}}";
            var user = LdJsonSerialization.DeserializeObject<User>(json);
            Assert.Equal(key, user.Key);
            Assert.Equal(0, user.Custom.Count);
            Assert.Equal(0, user.PrivateAttributeNames.Count);
        }

        [Theory]
        [MemberData(nameof(AllStringProperties))]
        public void CanDeserializeStringProperty(StringPropertyDesc p)
        {
            var value = "x";
            var jsonObject = LdValue.BuildObject().Add("key", "x").Add(p.Name, value).Build();
            var user = LdJsonSerialization.DeserializeObject<User>(jsonObject.ToJsonString());
            Assert.Equal(value, p.Getter(user));
        }

        [Fact]
        public void CanDeserializeCustomAttribute()
        {
            var json = $"{{\"key\":\"{key}\",\"custom\": {{\"a\":\"b\"}}}}";
            var user = LdJsonSerialization.DeserializeObject<User>(json);
            Assert.Equal("b", user.Custom["a"].AsString);
        }

        [Fact]
        public void CanDeserializePrivateAttribute()
        {
            var json = $"{{\"key\":\"{key}\",\"name\":\"Lucy\",\"privateAttributeNames\":[\"name\"]}}";
            var user = LdJsonSerialization.DeserializeObject<User>(json);
            Assert.Equal(new List<string> { "name" }, user.PrivateAttributeNames);
        }

        [Fact]
        public void CustomAttributesAndPrivateAttributesOmittedIfEmpty()
        {
            var user = User.WithKey(key);
            var expected = $"{{\"key\":\"{key}\"}}";
            TestUtil.AssertJsonEquals(expected, LdJsonSerialization.SerializeObject(user));
        }

        [Fact]
        public void AnonymousTrueIsSerializedAsTrue()
        {
            var user = User.Builder(key).Anonymous(true).Build();
            var expected = $"{{\"key\":\"{key}\",\"anonymous\":true}}";
            TestUtil.AssertJsonEquals(expected, LdJsonSerialization.SerializeObject(user));
        }

        [Fact]
        public void AnonymousFalseIsOmitted()
        {
            var user = User.Builder(key).Anonymous(false).Build();
            var expected = $"{{\"key\":\"{key}\"}}";
            TestUtil.AssertJsonEquals(expected, LdJsonSerialization.SerializeObject(user));
        }

        [Theory]
        [MemberData(nameof(AllStringProperties))]
        public void CanSerializeStringProperty(StringPropertyDesc p)
        {
            var value = "x";
            var user = p.Setter(User.Builder(key))(value).Build();
            var json = LdValue.Parse(LdJsonSerialization.SerializeObject(user));
            Assert.Equal(LdValue.Of(value), json.Get(p.Name));
        }

        [Fact]
        public void CustomAttributesAreSerialized()
        {
            LdValue value1 = LdValue.Of("hi");
            LdValue value2 = LdValue.Of(2);
            var user = User.Builder(key).Custom("name1", value1).Custom("name2", value2).Build();
            var json = LdValue.Parse(LdJsonSerialization.SerializeObject(user));
            Assert.Equal(LdValue.Of("hi"), json.Get("custom").Get("name1"));
            Assert.Equal(LdValue.Of(2), json.Get("custom").Get("name2"));
        }

        [Fact]
        public void PrivateAttributeNamesAreSerialized()
        {
            var user = User.Builder(key)
                .Name("user-name").AsPrivateAttribute()
                .Email("test@example.com").AsPrivateAttribute()
                .Build();
            var json = LdValue.Parse(LdJsonSerialization.SerializeObject(user));
            var names = new List<string>(json.Get("privateAttributeNames").AsList(LdValue.Convert.String));
            names.Sort();
            Assert.Equal(new List<string> { "email", "name" }, names);
        }
    }
}
