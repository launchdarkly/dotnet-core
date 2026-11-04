using System;
using System.Collections.Generic;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    public class FDv1FallbackTtlTest
    {
        private static readonly TimeSpan MinDefault = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan MaxDefault = TimeSpan.FromHours(1);

        private static void AssertIsDefault(TimeSpan ttl)
        {
            Assert.InRange(ttl, MinDefault, MaxDefault);
        }

        private static List<KeyValuePair<string, IEnumerable<string>>> Headers(string ttl) =>
            new List<KeyValuePair<string, IEnumerable<string>>>
            {
                new KeyValuePair<string, IEnumerable<string>>("x-ld-fd-fallback", new[] { "true" }),
                new KeyValuePair<string, IEnumerable<string>>("X-LD-FD-Fallback-TTL", new[] { ttl })
            };

        [Fact]
        public void DefaultIsJitteredWithinHalfToOneHour()
        {
            for (var i = 0; i < 100; i++)
            {
                AssertIsDefault(FDv1FallbackTtl.Default());
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0.5)]
        [InlineData(60)]
        [InlineData(3600)]
        public void ValidServiceTtlIsUsedAsIs(double seconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(seconds), FDv1FallbackTtl.FromServiceTtl(seconds));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(3601)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidServiceTtlUsesDefault(double seconds)
        {
            AssertIsDefault(FDv1FallbackTtl.FromServiceTtl(seconds));
        }

        [Fact]
        public void MissingServiceTtlUsesDefault()
        {
            AssertIsDefault(FDv1FallbackTtl.FromServiceTtl(null));
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("60", 60)]
        [InlineData(" 120 ", 120)]
        [InlineData("3600", 3600)]
        public void ValidHeaderTtlIsUsedAsIs(string header, int seconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(seconds), FDv1FallbackTtl.FromHeaders(Headers(header)));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("3601")]
        [InlineData("99999999999999999999")]
        [InlineData("1.5")]
        [InlineData("abc")]
        [InlineData("")]
        public void InvalidHeaderTtlUsesDefault(string header)
        {
            AssertIsDefault(FDv1FallbackTtl.FromHeaders(Headers(header)));
        }

        [Fact]
        public void MissingHeaderTtlUsesDefault()
        {
            var headers = new List<KeyValuePair<string, IEnumerable<string>>>
            {
                new KeyValuePair<string, IEnumerable<string>>("x-ld-fd-fallback", new[] { "true" })
            };

            AssertIsDefault(FDv1FallbackTtl.FromHeaders(headers));
            AssertIsDefault(FDv1FallbackTtl.FromHeaders(null));
        }
    }
}
