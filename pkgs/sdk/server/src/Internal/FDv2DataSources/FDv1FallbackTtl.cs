using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// Computes the Fallback TTL of an FDv1 Fallback Directive: how long to stay off FDv2 before
    /// attempting to recover to it.
    /// </summary>
    internal static class FDv1FallbackTtl
    {
        internal const string TtlHeaderName = "x-ld-fd-fallback-ttl";

        /// <summary>
        /// Used whenever the service supplies no usable TTL. Also the maximum valid TTL.
        /// </summary>
        internal static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);

        private static readonly Random Random = new Random();
        private static readonly object RandomLock = new object();

        /// <summary>
        /// Returns the default TTL, jittered by subtracting a value drawn uniformly from
        /// [0, DefaultTtl/2], so that SDKs which fell back together do not all retry at once.
        /// </summary>
        internal static TimeSpan Default()
        {
            double fraction;
            lock (RandomLock)
            {
                fraction = Random.NextDouble();
            }
            return DefaultTtl - TimeSpan.FromTicks((long)(DefaultTtl.Ticks / 2 * fraction));
        }

        /// <summary>
        /// Returns a service-supplied TTL as-is if it is within (0, 1 hour]; otherwise the
        /// jittered default, so a fallback is never indefinite.
        /// </summary>
        internal static TimeSpan FromServiceTtl(double? seconds)
        {
            if (seconds.HasValue && seconds.Value > 0 && seconds.Value <= DefaultTtl.TotalSeconds)
            {
                return TimeSpan.FromSeconds(seconds.Value);
            }
            return Default();
        }

        /// <summary>
        /// Returns the TTL carried by the X-LD-FD-Fallback-TTL header, which is a whole number
        /// of seconds. A missing or malformed header gives the jittered default.
        /// </summary>
        internal static TimeSpan FromHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            var value = headers?
                .Where(h => string.Equals(h.Key, TtlHeaderName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .FirstOrDefault();
            if (value != null &&
                long.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds))
            {
                return FromServiceTtl(seconds);
            }
            return Default();
        }
    }
}
