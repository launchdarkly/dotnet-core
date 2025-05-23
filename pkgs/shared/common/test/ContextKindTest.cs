using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class ContextKindTest
    {
        [Fact]
        public void UninitializedValueIsNotNull()
        {
            Assert.Equal("", new ContextKind().Value);
        }

        [Fact]
        public void NonEmptyValue()
        {
            Assert.Equal("abc", ContextKind.Of("abc").Value);
        }

        [Fact]
        public void NullOrEmptyBecomesDefault()
        {
            Assert.Equal("user", ContextKind.Of(null).Value);
            Assert.Equal("user", ContextKind.Of("").Value);
        }

        [Fact]
        public void IsDefault()
        {
            Assert.False(ContextKind.Of("abc").IsDefault);
            Assert.True(ContextKind.Of("user").IsDefault);
            Assert.True(ContextKind.Default.IsDefault);
        }

        [Fact]
        public void EqualsAndHashCode()
        {
            TypeBehavior.CheckEqualsAndHashCode(
                () => new ContextKind(),
                () => ContextKind.Default,
                () => ContextKind.Of("A"),
                () => ContextKind.Of("a"),
                () => ContextKind.Of("b")
                );
        }
    }
}
