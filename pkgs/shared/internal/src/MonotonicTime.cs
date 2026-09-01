using System;
using System.Diagnostics;

namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// Measurement of elapsed time from a clock source that is not affected by changes to the
    /// system clock, such as daylight saving transitions, NTP corrections, or manual clock changes.
    /// </summary>
    /// <remarks>
    /// Use this for any interval, deadline, or scheduling calculation. Wall clock types such as
    /// <see cref="DateTime.Now"/> are appropriate only for timestamps that are reported outside
    /// of the process.
    /// </remarks>
    public static class MonotonicTime
    {
        private static readonly double TicksPerStopwatchTick =
            (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

        /// <summary>
        /// Returns an opaque timestamp to be passed to <see cref="ElapsedSince(long)"/>.
        /// </summary>
        /// <returns>a timestamp with no meaning other than as an argument to <see cref="ElapsedSince(long)"/></returns>
        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        /// <summary>
        /// Returns the time that has elapsed since a timestamp previously returned by
        /// <see cref="GetTimestamp()"/>. The result is never negative.
        /// </summary>
        /// <param name="startingTimestamp">a timestamp from <see cref="GetTimestamp()"/></param>
        /// <returns>the elapsed time</returns>
        public static TimeSpan ElapsedSince(long startingTimestamp)
        {
            var elapsedStopwatchTicks = Stopwatch.GetTimestamp() - startingTimestamp;
            return elapsedStopwatchTicks <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromTicks((long)(elapsedStopwatchTicks * TicksPerStopwatchTick));
        }
    }
}
