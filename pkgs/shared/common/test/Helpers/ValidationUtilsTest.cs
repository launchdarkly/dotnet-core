using Xunit;

namespace LaunchDarkly.Sdk.Helpers
{
    public class ValidationUtilsTest
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsValidSdkKeyFormat_EmptyOrNullKey_ReturnsTrue(string key)
        {
            Assert.True(ValidationUtils.IsValidSdkKeyFormat(key));
        }

        [Theory]
        [InlineData("sdk-key-123")]
        [InlineData("sdk.key.123")]
        [InlineData("sdk_key_123")]
        [InlineData("SDKKEY123")]
        public void IsValidSdkKeyFormat_ValidKey_ReturnsTrue(string key)
        {
            Assert.True(ValidationUtils.IsValidSdkKeyFormat(key));
        }

        [Fact]
        public void IsValidSdkKeyFormat_TooLongKey_ReturnsFalseWithError()
        {
            var longKey = new string('a', 8193); // Creates a string longer than MaxSdkKeyLength (8192)
            Assert.False(ValidationUtils.IsValidSdkKeyFormat(longKey));
        }

        [Theory]
        [InlineData("sdk key")] // Contains space
        [InlineData("sdk#key")] // Contains special character
        [InlineData("sdk/key")] // Contains slash
        [InlineData("sdk\nkey")] // Contains newline
        public void IsValidSdkKeyFormat_InvalidCharacters_ReturnsFalseWithError(string key)
        {
            Assert.False(ValidationUtils.IsValidSdkKeyFormat(key));
        }

        [Theory]
        [InlineData("bad-\n")] // Contains newline
        [InlineData("bad-\t")] // Contains tab
        [InlineData("###invalid")] // Contains special characters
        [InlineData("")]  // Empty string
        [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEFwhoops")] // Too long
        [InlineData("#@$%^&")] // Invalid characters
        public void ValidateStringValue_Invalid_ReturnsErrorMessage(string input)
        {
            Assert.NotNull(ValidationUtils.ValidateStringValue(input));
        }

        [Theory]
        [InlineData("a-Az-Z0-9._-")]
        [InlineData("valid-string-123")]
        [InlineData("VALIDSTRING")]
        public void ValidateStringValue_Valid_ReturnsNull(string input)
        {
            Assert.Null(ValidationUtils.ValidateStringValue(input));
        }

        [Fact]
        public void SanitizeSpaces()
        {
            Assert.Equal("NoSpaces", ValidationUtils.SanitizeSpaces("NoSpaces"));
            Assert.Equal("Look-at-all-this-space", ValidationUtils.SanitizeSpaces("Look at all this space"));
            Assert.Equal("", ValidationUtils.SanitizeSpaces(""));
        }
    }
}
