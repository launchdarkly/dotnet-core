using System;
using LaunchDarkly.Sdk.Internal.Http;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Decides how long to wait between polling attempts, per the RETRY specification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There are two regimes. In the normal regime every wait is the configured poll interval:
    /// polling has no backoff of its own, because the interval already paces the requests. An
    /// <see cref="FailureClass.Unexpected"/> failure switches to the extended regime, where the
    /// wait starts at the extended initial interval and doubles per attempt up to one hour.
    /// </para>
    /// <para>
    /// Two consecutive successful polls return to the normal regime. One is not enough: a single
    /// success against a misconfigured environment should not discard the extended schedule.
    /// </para>
    /// <para>
    /// This type is not thread-safe. The polling loop drives it from a single task.
    /// </para>
    /// </remarks>
    internal sealed class PollingStrategy
    {
        internal static readonly TimeSpan ExtendedMaxDelay = TimeSpan.FromHours(1);

        private readonly TimeSpan _normalInterval;
        private readonly TimeSpan _extendedInitialInterval;
        private readonly Random _random;

        private int _n;
        private bool _priorPollWasSuccessful;
        private bool _inExtended;
        private TimeSpan _initialDelay;
        private TimeSpan _maxDelay;

        internal PollingStrategy(TimeSpan normalInterval, TimeSpan extendedInitialInterval)
            : this(normalInterval, extendedInitialInterval, new Random())
        {
        }

        /// <summary>
        /// Constructs an instance with a caller-supplied <see cref="Random"/>, so that tests can
        /// make jitter deterministic.
        /// </summary>
        internal PollingStrategy(TimeSpan normalInterval, TimeSpan extendedInitialInterval,
            Random random)
        {
            _normalInterval = normalInterval;
            _extendedInitialInterval = extendedInitialInterval;
            _random = random ?? new Random();

            // Normal regime: both bounds are the configured interval, so the formula yields that
            // interval for every attempt.
            _initialDelay = normalInterval;
            _maxDelay = normalInterval;
        }

        /// <summary>
        /// Records a failed poll.
        /// </summary>
        /// <param name="failureClass">how the failure was classified</param>
        /// <returns>
        /// true if this call moved the strategy from the normal regime into the extended one, so
        /// that the caller can log the transition exactly once
        /// </returns>
        internal bool OnFailure(FailureClass failureClass)
        {
            _priorPollWasSuccessful = false;
            if (failureClass == FailureClass.Unexpected && !_inExtended)
            {
                _inExtended = true;
                _n = 1;
                _initialDelay = Max(_extendedInitialInterval, _normalInterval);
                _maxDelay = Max(ExtendedMaxDelay, _normalInterval);
                return true;
            }
            _n++;
            return false;
        }

        /// <summary>
        /// Records a successful poll. Two consecutive successes return to the normal regime.
        /// </summary>
        internal void OnSuccess()
        {
            if (_priorPollWasSuccessful)
            {
                _n = 0;
                _inExtended = false;
                _initialDelay = _normalInterval;
                _maxDelay = _normalInterval;
            }
            _priorPollWasSuccessful = true;
        }

        /// <summary>
        /// Returns how long to wait before the next poll.
        /// </summary>
        /// <remarks>
        /// The wait is never shorter than the configured poll interval, so the extended regime can
        /// only ever slow polling down.
        /// </remarks>
        internal TimeSpan NextWait()
        {
            if (_n <= 0)
            {
                return _normalInterval;
            }

            var tMillis = UnjitteredMillis(
                (long)_initialDelay.TotalMilliseconds,
                (long)_maxDelay.TotalMilliseconds,
                _n);

            // Jitter is uniform in [0, T/2), so the wait lands in (T/2, T].
            var halfT = tMillis / 2;
            long jitterMillis = 0;
            if (halfT > 0)
            {
                // T is bounded by the one-hour ceiling, so half of it always fits in an int.
                var bound = halfT > int.MaxValue ? int.MaxValue : (int)halfT;
                jitterMillis = _random.Next(bound);
            }

            var waitMillis = tMillis - jitterMillis;
            var floorMillis = (long)_normalInterval.TotalMilliseconds;
            return TimeSpan.FromMilliseconds(waitMillis < floorMillis ? floorMillis : waitMillis);
        }

        /// <summary>
        /// Computes <c>initialDelay * 2^(n-1)</c> limited to <c>maxDelay</c>, in whole milliseconds.
        /// </summary>
        /// <remarks>
        /// Integer-only, comparing against the ceiling before shifting rather than computing the
        /// product and clamping it. <c>n</c> is unbounded -- a long outage keeps incrementing it --
        /// so computing the product first invites overflow. The <c>shifts >= 63</c> test is required
        /// rather than defensive: C# masks a 64-bit shift count to its low 6 bits, so
        /// <c>maxMillis >> 64</c> would silently mean <c>maxMillis >> 0</c>.
        /// </remarks>
        private static long UnjitteredMillis(long initialMillis, long maxMillis, int n)
        {
            if (initialMillis <= 0 || maxMillis <= 0)
            {
                return 0;
            }
            var shifts = n - 1;
            if (shifts <= 0)
            {
                return initialMillis > maxMillis ? maxMillis : initialMillis;
            }
            if (shifts >= 63 || initialMillis > (maxMillis >> shifts))
            {
                return maxMillis;
            }
            return initialMillis << shifts;
        }

        private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

        // Accessors for tests.
        internal int GetN() => _n;
        internal TimeSpan GetInitialDelay() => _initialDelay;
        internal TimeSpan GetMaxDelay() => _maxDelay;
        internal bool GetPriorPollWasSuccessful() => _priorPollWasSuccessful;
        internal bool GetInExtended() => _inExtended;
    }
}
