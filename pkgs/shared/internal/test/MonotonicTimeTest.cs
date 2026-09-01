using System;
using System.Threading;
using Xunit;

namespace LaunchDarkly.Sdk.Internal
{
    public class MonotonicTimeTest
    {
        [Fact]
        public void ElapsedTimeIsMeasured()
        {
            var start = MonotonicTime.GetTimestamp();
            Thread.Sleep(50);
            var elapsed = MonotonicTime.ElapsedSince(start);
            Assert.InRange(elapsed, TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void ElapsedTimeIsNeverNegative()
        {
            Assert.Equal(TimeSpan.Zero, MonotonicTime.ElapsedSince(MonotonicTime.GetTimestamp() + long.MaxValue / 2));
        }

        [Fact]
        public void ElapsedTimeIsUnaffectedByLocalTimeZoneChange()
        {
            if (!TemporaryTimeZone.IsSupported)
            {
                return;
            }

            using (new TemporaryTimeZone("Etc/GMT+5"))
            {
                var start = MonotonicTime.GetTimestamp();
                TemporaryTimeZone.SetLocalTimeZone("Etc/GMT-5");
                Assert.InRange(MonotonicTime.ElapsedSince(start), TimeSpan.Zero, TimeSpan.FromSeconds(5));
            }
        }
    }
}
