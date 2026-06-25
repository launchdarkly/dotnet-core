using System;
using Xunit;

namespace LaunchDarkly.Sdk.Internal
{
    public class UriExtensionsTest
    {
        [Theory]
        [InlineData("http://hostname", "/a/b", "http://hostname/a/b")]
        [InlineData("http://hostname", "a/b", "http://hostname/a/b")]
        [InlineData("http://hostname:8080", "/a/b", "http://hostname:8080/a/b")]
        [InlineData("http://hostname:8080", "a/b", "http://hostname:8080/a/b")]
        [InlineData("http://hostname/", "/a/b", "http://hostname/a/b")]
        [InlineData("http://hostname/", "a/b", "http://hostname/a/b")]
        [InlineData("http://hostname/a", "/b", "http://hostname/a/b")]
        [InlineData("http://hostname/a", "b", "http://hostname/a/b")]
        [InlineData("http://hostname/a/", "/b", "http://hostname/a/b")]
        [InlineData("http://hostname/a/", "b", "http://hostname/a/b")]
        public void AddPath(string originalUri, string addedPath, string expectedResult) =>
            Assert.Equal(new Uri(expectedResult), new Uri(originalUri).AddPath(addedPath));

        [Theory]
        [InlineData("http://hostname/a", "b", "http://hostname/a?b")]
        [InlineData("http://hostname/a?c", "b", "http://hostname/a?c&b")]
        public void AddQuery(string originalUri, string addedQuery, string expectedResult) =>
            Assert.Equal(new Uri(expectedResult), new Uri(originalUri).AddQuery(addedQuery));
    }
}
