using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using LaunchDarkly.Sdk.Internal.Http;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    public class PollingStrategyTest
    {
        private static readonly TimeSpan Normal30s = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan Extended5m = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

        /// <summary>
        /// Returns zero jitter, so <c>NextWait</c> yields the un-jittered delay exactly and the
        /// progression can be asserted without ranges.
        /// </summary>
        private sealed class NoJitter : Random
        {
            public override int Next(int maxValue) => 0;
        }

        private static PollingStrategy Strategy(TimeSpan? normal = null, TimeSpan? extended = null) =>
            new PollingStrategy(normal ?? Normal30s, extended ?? Extended5m, new NoJitter());

        #region Normal regime

        [Fact]
        public void FreshStrategyWaitsThePollInterval() =>
            Assert.Equal(Normal30s, Strategy().NextWait());

        [Fact]
        public void NormalFailureDoesNotEngageExtended()
        {
            var s = Strategy();

            Assert.False(s.OnFailure(FailureClass.Normal));

            Assert.False(s.GetInExtended());
            Assert.Equal(Normal30s, s.GetInitialDelay());
            Assert.Equal(Normal30s, s.GetMaxDelay());
        }

        [Fact]
        public void NormalFailuresKeepWaitingThePollInterval()
        {
            var s = Strategy();

            // The normal regime has no backoff of its own: both bounds are the poll interval, so
            // the formula yields that interval no matter how far n advances.
            for (var i = 0; i < 5; i++)
            {
                s.OnFailure(FailureClass.Normal);
                Assert.Equal(Normal30s, s.NextWait());
            }
        }

        [Fact]
        public void NormalFailuresStillAdvanceN()
        {
            var s = Strategy();

            s.OnFailure(FailureClass.Normal);
            s.OnFailure(FailureClass.Normal);

            Assert.Equal(2, s.GetN());
        }

        #endregion

        #region Transition into the extended regime

        [Fact]
        public void UnexpectedFailureEngagesExtended()
        {
            var s = Strategy();

            Assert.True(s.OnFailure(FailureClass.Unexpected));

            Assert.True(s.GetInExtended());
            Assert.Equal(1, s.GetN());
            Assert.Equal(Extended5m, s.GetInitialDelay());
            Assert.Equal(OneHour, s.GetMaxDelay());
            Assert.Equal(Extended5m, s.NextWait());
        }

        [Fact]
        public void UnexpectedAfterNormalFailuresStartsAtTheExtendedInitial()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Normal);
            s.OnFailure(FailureClass.Normal);
            s.OnFailure(FailureClass.Normal);

            s.OnFailure(FailureClass.Unexpected);

            // n is reset to 1 on the transition, so the first extended wait is the extended
            // initial rather than the initial already doubled by the preceding normal failures.
            Assert.Equal(1, s.GetN());
            Assert.Equal(Extended5m, s.NextWait());
        }

        [Fact]
        public void OnFailureReturnsTrueOnlyOnTheTransition()
        {
            var s = Strategy();

            Assert.True(s.OnFailure(FailureClass.Unexpected));
            Assert.False(s.OnFailure(FailureClass.Unexpected));
            Assert.False(s.OnFailure(FailureClass.Unexpected));
            Assert.False(s.OnFailure(FailureClass.Normal));
        }

        [Fact]
        public void UnexpectedWhileAlreadyExtendedContinuesDoubling()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Unexpected);

            s.OnFailure(FailureClass.Unexpected);

            Assert.Equal(2, s.GetN());
            Assert.Equal(TimeSpan.FromMinutes(10), s.NextWait());
        }

        #endregion

        #region Extended regime progression

        [Fact]
        public void ExtendedRegimeDoublesEachAttempt()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Unexpected);

            Assert.Equal(TimeSpan.FromMinutes(5), s.NextWait());
            s.OnFailure(FailureClass.Unexpected);
            Assert.Equal(TimeSpan.FromMinutes(10), s.NextWait());
            s.OnFailure(FailureClass.Unexpected);
            Assert.Equal(TimeSpan.FromMinutes(20), s.NextWait());
            s.OnFailure(FailureClass.Unexpected);
            Assert.Equal(TimeSpan.FromMinutes(40), s.NextWait());
        }

        [Fact]
        public void ExtendedRegimeClampsAtOneHour()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Unexpected);
            for (var i = 0; i < 10; i++)
            {
                s.OnFailure(FailureClass.Unexpected);
            }

            Assert.Equal(OneHour, s.NextWait());
        }

        [Fact]
        public void ExtendedInitialIsRaisedToThePollIntervalWhenSmaller()
        {
            var s = Strategy(normal: TimeSpan.FromMinutes(10), extended: Extended5m);

            s.OnFailure(FailureClass.Unexpected);

            // The extended regime must never poll faster than the configured interval.
            Assert.Equal(TimeSpan.FromMinutes(10), s.GetInitialDelay());
            Assert.Equal(TimeSpan.FromMinutes(10), s.NextWait());
        }

        [Fact]
        public void DoublingStillAppliesWhenTheExtendedInitialWasRaised()
        {
            var s = Strategy(normal: TimeSpan.FromMinutes(10), extended: Extended5m);
            s.OnFailure(FailureClass.Unexpected);

            s.OnFailure(FailureClass.Unexpected);
            Assert.Equal(TimeSpan.FromMinutes(20), s.NextWait());
            s.OnFailure(FailureClass.Unexpected);
            Assert.Equal(TimeSpan.FromMinutes(40), s.NextWait());
        }

        [Fact]
        public void DoublingStillAppliesWhenPollIntervalEqualsExtendedInitial()
        {
            var s = Strategy(normal: Extended5m, extended: Extended5m);
            s.OnFailure(FailureClass.Unexpected);

            Assert.Equal(TimeSpan.FromMinutes(5), s.NextWait());
            s.OnFailure(FailureClass.Unexpected);
            Assert.Equal(TimeSpan.FromMinutes(10), s.NextWait());
        }

        [Fact]
        public void ExtendedRegimeCollapsesWhenPollIntervalExceedsTheCeiling()
        {
            var twoHours = TimeSpan.FromHours(2);
            var s = Strategy(normal: twoHours, extended: Extended5m);

            s.OnFailure(FailureClass.Unexpected);

            // Both bounds become the poll interval, so there is nothing left to escalate.
            Assert.Equal(twoHours, s.GetInitialDelay());
            Assert.Equal(twoHours, s.GetMaxDelay());
            Assert.Equal(twoHours, s.NextWait());
            s.OnFailure(FailureClass.Unexpected);
            Assert.Equal(twoHours, s.NextWait());
        }

        [Fact]
        public void WaitIsNeverShorterThanThePollInterval()
        {
            // Jitter can remove up to half of T; the floor keeps it at the configured interval.
            var s = new PollingStrategy(Normal30s, Extended5m, new Random(12345));

            for (var i = 0; i < 20; i++)
            {
                s.OnFailure(FailureClass.Normal);
                Assert.True(s.NextWait() >= Normal30s);
            }
        }

        #endregion

        #region Returning to the normal regime

        [Fact]
        public void FirstSuccessDoesNotLeaveExtended()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Unexpected);

            s.OnSuccess();

            Assert.True(s.GetInExtended());
            Assert.True(s.GetPriorPollWasSuccessful());
            Assert.Equal(Extended5m, s.NextWait());
        }

        [Fact]
        public void TwoConsecutiveSuccessesReturnToNormal()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Unexpected);

            s.OnSuccess();
            s.OnSuccess();

            Assert.False(s.GetInExtended());
            Assert.Equal(0, s.GetN());
            Assert.Equal(Normal30s, s.GetInitialDelay());
            Assert.Equal(Normal30s, s.GetMaxDelay());
            Assert.Equal(Normal30s, s.NextWait());
        }

        [Fact]
        public void FailureBetweenSuccessesClearsTheResetGate()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Unexpected);

            s.OnSuccess();
            s.OnFailure(FailureClass.Normal);
            s.OnSuccess();

            // The two successes were not consecutive, so the extended regime still stands.
            Assert.True(s.GetInExtended());
        }

        [Fact]
        public void NormalFailureAfterAResetStaysInNormal()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Unexpected);
            s.OnSuccess();
            s.OnSuccess();

            Assert.False(s.OnFailure(FailureClass.Normal));

            Assert.False(s.GetInExtended());
            Assert.Equal(Normal30s, s.NextWait());
        }

        [Fact]
        public void ResetReArmsTheExtendedTransition()
        {
            var s = Strategy();
            s.OnFailure(FailureClass.Unexpected);
            s.OnSuccess();
            s.OnSuccess();

            // Having returned to normal, a later unexpected failure is a fresh transition and must
            // report itself as one so the caller logs it again.
            Assert.True(s.OnFailure(FailureClass.Unexpected));
            Assert.Equal(Extended5m, s.NextWait());
        }

        #endregion

        #region Delay arithmetic

        [Fact]
        public void UnjitteredDelayMatchesAnExactReference()
        {
            // The implementation compares against the ceiling before shifting rather than
            // computing initial * 2^(n-1) and clamping, so that no intermediate can overflow.
            // BigInteger cannot overflow, so it settles whether the shortcut is exact.
            var method = typeof(PollingStrategy).GetMethod("UnjitteredMillis",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var interesting = new List<long>
            {
                0, 1, 2, 3, 999, 1000, 30000, 300000, 3600000, 7200000,
                int.MaxValue - 1L, int.MaxValue, int.MaxValue + 1L, 4294967294L,
                922337203685477L, long.MaxValue / 2,
                (1L << 62) - 1, 1L << 62, (1L << 62) + 1, long.MaxValue
            };
            // Seeded, so a failure is reproducible. Random magnitudes across the whole long range
            // catch cases curated values would not think to include.
            var random = new Random(12345);
            for (var i = 0; i < 120; i++)
            {
                interesting.Add((long)(random.NextDouble() * long.MaxValue));
            }
            var failures = new List<string>();

            foreach (var initial in interesting)
            {
                foreach (var max in interesting)
                {
                    foreach (var n in new[]
                        { -1, 0, 1, 2, 3, 4, 10, 21, 31, 32, 40, 62, 63, 64, 65, 100, 1000, 100000, int.MaxValue })
                    {
                        var actual = (long)method.Invoke(null, new object[] { initial, max, n });
                        var expected = Reference(initial, max, n);
                        if (actual != expected && failures.Count < 20)
                        {
                            failures.Add($"initial={initial} max={max} n={n} actual={actual} expected={expected}");
                        }
                    }
                }
            }
            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }

        private static long Reference(long initialMillis, long maxMillis, int n)
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
            if (shifts >= 64)
            {
                // initialMillis is at least 1 here, so the product is at least 2^64, which exceeds
                // long.MaxValue and therefore exceeds maxMillis. Short-circuiting keeps the shift
                // below from allocating an astronomically wide BigInteger. Deliberately 64 rather
                // than the implementation's 63, so shifts == 63 is still checked against the
                // arbitrary-precision result rather than assumed.
                return maxMillis;
            }
            var exact = new BigInteger(initialMillis) << shifts;
            return exact > new BigInteger(maxMillis) ? maxMillis : (long)exact;
        }

        [Fact]
        public void JitterKeepsTheWaitInTheUpperHalfOfT()
        {
            // T is 5 minutes on the first extended attempt, and jitter is drawn from [0, T/2),
            // so the wait must land in (T/2, T].
            var s = new PollingStrategy(TimeSpan.FromMilliseconds(1), Extended5m, new Random(12345));
            s.OnFailure(FailureClass.Unexpected);

            var min = TimeSpan.MaxValue;
            var max = TimeSpan.MinValue;
            for (var i = 0; i < 2000; i++)
            {
                var wait = s.NextWait();
                if (wait < min) { min = wait; }
                if (wait > max) { max = wait; }
            }

            Assert.True(min > TimeSpan.FromMinutes(2.5) - TimeSpan.FromMilliseconds(1),
                $"minimum wait was {min}");
            Assert.True(max <= Extended5m, $"maximum wait was {max}");
        }

        #endregion
    }
}
