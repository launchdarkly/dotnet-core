using Xunit;

namespace LaunchDarkly.Sdk.Helpers
{
    public class ValidationUtilsTest
    {
        [Fact]
        public void ValidateStringValue()
        {
            Assert.NotNull(ValidationUtils.ValidateStringValue("bad-\n"));
            Assert.NotNull(ValidationUtils.ValidateStringValue("bad-\t"));
            Assert.NotNull(ValidationUtils.ValidateStringValue("###invalid"));
            Assert.NotNull(ValidationUtils.ValidateStringValue(""));
            Assert.NotNull(
                ValidationUtils.ValidateStringValue(
                    "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEFwhoops"));
            Assert.NotNull(ValidationUtils.ValidateStringValue("#@$%^&"));
            Assert.Null(ValidationUtils.ValidateStringValue("a-Az-Z0-9._-"));
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
