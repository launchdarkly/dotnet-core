using System;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// Class used for event sampling.
    /// </summary>
    public static class Sampler
    {
        private static readonly Random Rand = new Random();
        private static readonly object RandLock = new object();

        /// <summary>
        /// Given a ratio determine if an event should be sampled.
        /// </summary>
        /// <remarks>This function is thread-safe.</remarks>
        /// <remarks>0 means never sample and 1 means always sample</remarks>
        /// <param name="samplingRatio">the sampling ratio</param>
        /// <returns>true if it should be sampled</returns>
        public static bool Sample(long samplingRatio)
        {
            if (samplingRatio <= 0) return false;
            if (samplingRatio == 1) return true;
            // Random instances are not thread-safe.
            // https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-6.0#ThreadSafety
            lock (RandLock)
            {
                #if NET6_0_OR_GREATER
                return Rand.NextInt64(samplingRatio) == 0;
                #else
                // Prior to .Net 6 there was not a 64 bit rand method.
                // So this caps it to int.MaxValue.
                return Rand.Next(
                    (int)Math.Min(int.MaxValue, samplingRatio)) == 0;
                #endif
            }
        }
    }
}
