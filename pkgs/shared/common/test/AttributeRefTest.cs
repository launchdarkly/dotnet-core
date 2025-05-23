using System;
using LaunchDarkly.Sdk.Json;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class AttributeRefTest
    {
        [Fact]
        public void UninitializedRef()
        {
            var a = new AttributeRef();
            Assert.False(a.Defined);
            Assert.False(a.Valid);
            Assert.Equal(Errors.AttrEmpty, a.Error);
            Assert.Equal("", a.ToString());
            Assert.Equal(0, a.Depth);
        }

        [Theory]
        [InlineData("", Errors.AttrEmpty)]
        [InlineData("/", Errors.AttrEmpty)]
        [InlineData("//", Errors.AttrExtraSlash)]
        [InlineData("/a//b", Errors.AttrExtraSlash)]
        [InlineData("/a/b/", Errors.AttrExtraSlash)]
        [InlineData("/a~x", Errors.AttrInvalidEscape)]
        [InlineData("/a~", Errors.AttrInvalidEscape)]
        [InlineData("/a/b~x", Errors.AttrInvalidEscape)]
        [InlineData("/a/b~", Errors.AttrInvalidEscape)]
        public void InvalidRef(string s, string expectedError)
        {
            var a = AttributeRef.FromPath(s);
            Assert.True(a.Defined);
            Assert.False(a.Valid);
            Assert.Equal(expectedError, a.Error);
            Assert.Equal(s, a.ToString());
            Assert.Equal(0, a.Depth);
        }

        [Theory]
        [InlineData("name")]
        [InlineData("name/with/slashes")]
        [InlineData("name~0~1with-what-looks-like-escape-sequences")]
        public void RefWithNoLeadingSlash(string s)
        {
            var a = AttributeRef.FromPath(s);
            Assert.True(a.Defined);
            Assert.True(a.Valid);
            Assert.Null(a.Error);
            Assert.Equal(s, a.ToString());
            Assert.Equal(1, a.Depth);
            Assert.Equal(s, a.GetComponent(0));
        }

        [Theory]
        [InlineData("/name", "name")]
        [InlineData("/0", "0")]
        [InlineData("/name~1with~1slashes~0and~0tildes", "name/with/slashes~and~tildes")]
        public void RefSimpleWithLeadingSlash(string s, string unescaped)
        {
            var a = AttributeRef.FromPath(s);
            Assert.True(a.Defined);
            Assert.True(a.Valid);
            Assert.Null(a.Error);
            Assert.Equal(s, a.ToString());
            Assert.Equal(1, a.Depth);
            Assert.Equal(unescaped, a.GetComponent(0));
        }

        [Fact]
        public void Literal()
        {
            var a0 = AttributeRef.FromLiteral("name");
            Assert.Equal(AttributeRef.FromPath("name"), a0);

            var a1 = AttributeRef.FromLiteral("a/b");
            Assert.Equal(AttributeRef.FromPath("a/b"), a1);

            var a2 = AttributeRef.FromLiteral("/a/b~c");
            Assert.Equal(AttributeRef.FromPath("/~1a~1b~0c"), a2);
            Assert.Equal(1, a2.Depth);

            var a3 = AttributeRef.FromLiteral("/");
            Assert.Equal(AttributeRef.FromPath("/~1"), a3);

            var a4 = AttributeRef.FromLiteral("");
            Assert.Equal(Errors.AttrEmpty, a4.Error);
        }

        [Theory]
        [InlineData("", 0, 0, null)]
        [InlineData("key", 1, 0, "key")]
        [InlineData("/key", 1, 0, "key")]
        [InlineData("/a/b", 2, 0, "a")]
        [InlineData("/a/b", 2, 1, "b")]
        [InlineData("/a~1b/c", 2, 0, "a/b")]
        [InlineData("/a~0b/c", 2, 0, "a~b")]
        [InlineData("/a/10/20/30x", 4, 1, "10")]
        [InlineData("/a/10/20/30x", 4, 2, "20")]
        [InlineData("/a/10/20/30x", 4, 3, "30x")]
        [InlineData("", 0, -1, null)]
        [InlineData("key", 1, -1, null)]
        [InlineData("key", 1, 1, null)]
        [InlineData("/key", 1, -1, null)]
        [InlineData("/key", 1, 1, null)]
        [InlineData("/a/b", 2, -1, null)]
        [InlineData("/a/b", 2, 2, null)]
        public void TryGetComponent(string input, int depth, int index, string expectedName)
        {
            var a = AttributeRef.FromPath(input);
            Assert.Equal(depth, a.Depth);
            Assert.Equal(expectedName, a.GetComponent(index));
        }

        [Fact]
        public void Equality()
        {
            TypeBehavior.CheckEqualsAndHashCode(
                () => new AttributeRef(),
                () => AttributeRef.FromPath(""),
                () => AttributeRef.FromPath("a"),
                () => AttributeRef.FromPath("b"),
                () => AttributeRef.FromPath("/a/b"),
                () => AttributeRef.FromPath("/a/c"),
                () => AttributeRef.FromPath("///")
                );
        }

        [Theory]
        [InlineData(null, "null")]
        [InlineData("a", @"""a""")]
        [InlineData("/a/b", @"""/a/b""")]
        [InlineData("///invalid", @"""///invalid""")]
        public void SerializeJson(string attrPath, string expected)
        {
            var a = attrPath is null ? new AttributeRef() : AttributeRef.FromPath(attrPath);
            Assert.Equal(expected, LdJsonSerialization.SerializeObject(a));
        }

        [Theory]
        [InlineData("null", null, true)]
        [InlineData(@"""a""", "a", true)]
        [InlineData(@"""/a/b""", "/a/b", true)]
        [InlineData(@"""///invalid""", "///invalid", true)]
        [InlineData("true", null, false)]
        [InlineData("2", null, false)]
        [InlineData("[]", null, false)]
        [InlineData("{}", null, false)]
        [InlineData(".", null, false)]
        [InlineData("", null, false)]
        public void DeserializeJson(string json, string attrPath, bool success)
        {
            if (success)
            {
                var a = LdJsonSerialization.DeserializeObject<AttributeRef>(json);
                Assert.Equal(AttributeRef.FromPath(attrPath), a);
            }
            else
            {
                Assert.ThrowsAny<Exception>(() => LdJsonSerialization.DeserializeObject<AttributeRef>(json));
            }
        }
    }
}
