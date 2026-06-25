using Xunit;

namespace LaunchDarkly.Sdk.Internal.Concurrent
{
    public class AtomicBooleanTest
    {
        [Fact]
        public void InitialValue()
        {
            Assert.False(new AtomicBoolean(false).Get());
            Assert.True(new AtomicBoolean(true).Get());
        }

        [Fact]
        public void GetAndSet()
        {
            var ab = new AtomicBoolean(false);
            Assert.False(ab.GetAndSet(false));
            Assert.False(ab.GetAndSet(true));
            Assert.True(ab.Get());
            Assert.True(ab.GetAndSet(false));
            Assert.False(ab.Get());
        }
    }
}
