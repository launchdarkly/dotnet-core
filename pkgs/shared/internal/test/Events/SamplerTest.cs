using Xunit;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class SamplerTest
    {
        [Fact]
        public void ItDoesNotSampleARatioOfZero()
        {
            Assert.False(Sampler.Sample(0));
        }

        [Fact]
        public void ItDoesSampleARatioOfOne()
        {
            Assert.True(Sampler.Sample(1));
        }

        [Fact]
        public void ItProbabilisticallySamples()
        {
            var sampledCount = 0;
            const int trialCount = 10000;
            for (int i = 0; i < trialCount; i++)
            {
                if (Sampler.Sample(2))
                {
                    sampledCount++;
                }
            }

            Assert.Equal(0.5, (double) sampledCount / trialCount, 0.2);
        }

        [Fact]
        public void ItHandlesMaxRange()
        {
            Sampler.Sample(long.MaxValue);
        }

        [Fact]
        public void ItTreatsNegativeValuesLikeZero()
        {
            Assert.False(Sampler.Sample(-10));
        }
    }
}
