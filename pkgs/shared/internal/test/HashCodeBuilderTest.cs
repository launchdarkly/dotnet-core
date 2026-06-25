using Xunit;

namespace LaunchDarkly.Sdk.Internal
{
    public class HashCodeBuilderTest
    {
        [Fact]
        public void DefaultIsZero()
        {
            Assert.Equal(0, HashCodeBuilder.New().Value);
        }

        [Fact]
        public void BuildValues()
        {
            int value1 = 123;
            string value2 = "xyz";
            Assert.Equal(value1.GetHashCode() * 17 + value2.GetHashCode(),
                HashCodeBuilder.New().With(value1).With(value2).Value);
        }

        [Fact]
        public void ValuesCanBeNull()
        {
            int value1 = 123;
            string value3 = "xyz";
            Assert.Equal(value1.GetHashCode() * 17 * 17 + value3.GetHashCode(),
                HashCodeBuilder.New().With(value1).With(null).With(value3).Value);
        }
    }
}
