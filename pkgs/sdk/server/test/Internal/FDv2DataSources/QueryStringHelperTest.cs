using System.Collections.Generic;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    public class QueryStringHelperTest
    {
        [Fact]
        public void ParseQueryString_EmptyString()
        {
            var result = QueryStringHelper.ParseQueryString("");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseQueryString_Null()
        {
            var result = QueryStringHelper.ParseQueryString(null);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseQueryString_OnlyQuestionMark()
        {
            var result = QueryStringHelper.ParseQueryString("?");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseQueryString_SingleParameter()
        {
            var result = QueryStringHelper.ParseQueryString("key=value");
            Assert.Single(result);
            Assert.Equal("value", result["key"]);
        }

        [Fact]
        public void ParseQueryString_SingleParameterWithQuestionMark()
        {
            var result = QueryStringHelper.ParseQueryString("?key=value");
            Assert.Single(result);
            Assert.Equal("value", result["key"]);
        }

        [Fact]
        public void ParseQueryString_MultipleParameters()
        {
            var result = QueryStringHelper.ParseQueryString("key1=value1&key2=value2&key3=value3");
            Assert.Equal(3, result.Count);
            Assert.Equal("value1", result["key1"]);
            Assert.Equal("value2", result["key2"]);
            Assert.Equal("value3", result["key3"]);
        }

        [Fact]
        public void ParseQueryString_ParameterWithoutValue()
        {
            var result = QueryStringHelper.ParseQueryString("key");
            Assert.Single(result);
            Assert.Equal("", result["key"]);
        }

        [Fact]
        public void ParseQueryString_ParameterWithEmptyValue()
        {
            var result = QueryStringHelper.ParseQueryString("key=");
            Assert.Single(result);
            Assert.Equal("", result["key"]);
        }

        [Fact]
        public void ParseQueryString_MixedParametersWithAndWithoutValues()
        {
            var result = QueryStringHelper.ParseQueryString("key1=value1&key2&key3=");
            Assert.Equal(3, result.Count);
            Assert.Equal("value1", result["key1"]);
            Assert.Equal("", result["key2"]);
            Assert.Equal("", result["key3"]);
        }

        [Fact]
        public void ParseQueryString_UrlEncodedValues()
        {
            var result = QueryStringHelper.ParseQueryString("name=John%20Doe&email=test%40example.com");
            Assert.Equal(2, result.Count);
            Assert.Equal("John Doe", result["name"]);
            Assert.Equal("test@example.com", result["email"]);
        }

        [Fact]
        public void ParseQueryString_UrlEncodedKeys()
        {
            var result = QueryStringHelper.ParseQueryString("my%20key=value&another%2Bkey=data");
            Assert.Equal(2, result.Count);
            Assert.Equal("value", result["my key"]);
            Assert.Equal("data", result["another+key"]);
        }

        [Fact]
        public void ParseQueryString_SpecialCharacters()
        {
            var result = QueryStringHelper.ParseQueryString("key=%3D%26%3F%23");
            Assert.Single(result);
            Assert.Equal("=&?#", result["key"]);
        }

        [Fact]
        public void ParseQueryString_DuplicateKeys()
        {
            var result = QueryStringHelper.ParseQueryString("key=first&key=second");
            Assert.Single(result);
            Assert.Equal("second", result["key"]);
        }

        [Fact]
        public void ParseQueryString_CaseInsensitiveKeys()
        {
            var result = QueryStringHelper.ParseQueryString("Key=first&KEY=second");
            Assert.Single(result);
            Assert.Equal("second", result["Key"]);
            Assert.Equal("second", result["key"]);
        }

        [Fact]
        public void ParseQueryString_MultipleEquals()
        {
            var result = QueryStringHelper.ParseQueryString("key=value=with=equals");
            Assert.Single(result);
            Assert.Equal("value=with=equals", result["key"]);
        }

        [Fact]
        public void ParseQueryString_EmptyParametersBetweenAmpersands()
        {
            var result = QueryStringHelper.ParseQueryString("key1=value1&&key2=value2");
            Assert.Equal(3, result.Count);
            Assert.Equal("value1", result["key1"]);
            Assert.Equal("value2", result["key2"]);
            Assert.Equal("", result[""]);
        }

        [Fact]
        public void ParseQueryString_TrailingAmpersand()
        {
            var result = QueryStringHelper.ParseQueryString("key1=value1&");
            Assert.Equal(2, result.Count);
            Assert.Equal("value1", result["key1"]);
            Assert.Equal("", result[""]);
        }

        [Fact]
        public void ToQueryString_EmptyDictionary()
        {
            var result = QueryStringHelper.ToQueryString(new Dictionary<string, string>());
            Assert.Equal("", result);
        }

        [Fact]
        public void ToQueryString_Null()
        {
            var result = QueryStringHelper.ToQueryString(null);
            Assert.Equal("", result);
        }

        [Fact]
        public void ToQueryString_SingleParameter()
        {
            var parameters = new Dictionary<string, string> { { "key", "value" } };
            var result = QueryStringHelper.ToQueryString(parameters);
            Assert.Equal("key=value", result);
        }

        [Fact]
        public void ToQueryString_MultipleParameters()
        {
            var parameters = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" },
                { "key3", "value3" }
            };
            var result = QueryStringHelper.ToQueryString(parameters);
            var parsed = QueryStringHelper.ParseQueryString(result);
            Assert.Equal(3, parsed.Count);
            Assert.Equal("value1", parsed["key1"]);
            Assert.Equal("value2", parsed["key2"]);
            Assert.Equal("value3", parsed["key3"]);
        }

        [Fact]
        public void ToQueryString_EmptyValue()
        {
            var parameters = new Dictionary<string, string> { { "key", "" } };
            var result = QueryStringHelper.ToQueryString(parameters);
            Assert.Equal("key=", result);
        }

        [Fact]
        public void ToQueryString_NullValue()
        {
            var parameters = new Dictionary<string, string> { { "key", null } };
            var result = QueryStringHelper.ToQueryString(parameters);
            Assert.Equal("key=", result);
        }

        [Fact]
        public void ToQueryString_SpecialCharactersInValue()
        {
            var parameters = new Dictionary<string, string> { { "name", "John Doe" } };
            var result = QueryStringHelper.ToQueryString(parameters);
            Assert.Equal("name=John%20Doe", result);
        }

        [Fact]
        public void ToQueryString_SpecialCharactersInKey()
        {
            var parameters = new Dictionary<string, string> { { "my key", "value" } };
            var result = QueryStringHelper.ToQueryString(parameters);
            Assert.Equal("my%20key=value", result);
        }

        [Fact]
        public void ToQueryString_UrlCharactersEncoded()
        {
            var parameters = new Dictionary<string, string> { { "url", "=&?#" } };
            var result = QueryStringHelper.ToQueryString(parameters);
            Assert.Contains("%3D", result);
            Assert.Contains("%26", result);
            Assert.Contains("%3F", result);
            Assert.Contains("%23", result);
        }

        [Fact]
        public void RoundTrip_SimpleParameters()
        {
            var original = "key1=value1&key2=value2&key3=value3";
            var parsed = QueryStringHelper.ParseQueryString(original);
            var reconstructed = QueryStringHelper.ToQueryString(parsed);
            var reparsed = QueryStringHelper.ParseQueryString(reconstructed);

            Assert.Equal(parsed.Count, reparsed.Count);
            foreach (var kvp in parsed)
            {
                Assert.Equal(kvp.Value, reparsed[kvp.Key]);
            }
        }

        [Fact]
        public void RoundTrip_SpecialCharacters()
        {
            var original = new Dictionary<string, string>
            {
                { "name", "John Doe" },
                { "email", "test@example.com" },
                { "url", "https://example.com/path?query=1" },
                { "special", "=&?#%+" }
            };

            var queryString = QueryStringHelper.ToQueryString(original);
            var parsed = QueryStringHelper.ParseQueryString(queryString);

            Assert.Equal(original.Count, parsed.Count);
            foreach (var kvp in original)
            {
                Assert.Equal(kvp.Value, parsed[kvp.Key]);
            }
        }

        [Fact]
        public void RoundTrip_EmptyAndNullValues()
        {
            var original = new Dictionary<string, string>
            {
                { "key1", "value" },
                { "key2", "" },
                { "key3", null }
            };

            var queryString = QueryStringHelper.ToQueryString(original);
            var parsed = QueryStringHelper.ParseQueryString(queryString);

            Assert.Equal(3, parsed.Count);
            Assert.Equal("value", parsed["key1"]);
            Assert.Equal("", parsed["key2"]);
            Assert.Equal("", parsed["key3"]);
        }

        [Fact]
        public void ToQueryString_DoesNotAddLeadingQuestionMark()
        {
            var parameters = new Dictionary<string, string> { { "key", "value" } };
            var result = QueryStringHelper.ToQueryString(parameters);
            Assert.False(result.StartsWith("?"));
        }

        [Fact]
        public void ParseQueryString_Unicode()
        {
            var result = QueryStringHelper.ParseQueryString("name=%E4%BD%A0%E5%A5%BD&city=%E6%9D%B1%E4%BA%AC");
            Assert.Equal(2, result.Count);
            Assert.Equal("你好", result["name"]);
            Assert.Equal("東京", result["city"]);
        }

        [Fact]
        public void ToQueryString_Unicode()
        {
            var parameters = new Dictionary<string, string>
            {
                { "name", "你好" },
                { "city", "東京" }
            };
            var result = QueryStringHelper.ToQueryString(parameters);
            var parsed = QueryStringHelper.ParseQueryString(result);
            Assert.Equal("你好", parsed["name"]);
            Assert.Equal("東京", parsed["city"]);
        }

        [Fact]
        public void ParseQueryString_PlusSign()
        {
            var result = QueryStringHelper.ParseQueryString("key=value+with+plus");
            Assert.Single(result);
            Assert.Equal("value+with+plus", result["key"]);
        }

        [Fact]
        public void ParseQueryString_PercentEncodedSpace()
        {
            var result = QueryStringHelper.ParseQueryString("key=value%20with%20space");
            Assert.Single(result);
            Assert.Equal("value with space", result["key"]);
        }
    }
}