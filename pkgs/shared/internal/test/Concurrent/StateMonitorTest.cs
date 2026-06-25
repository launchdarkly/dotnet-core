using System;
using System.Threading;
using LaunchDarkly.Logging;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Internal.Concurrent
{
    public class StateMonitorTest
    {
        private static readonly object[] Immediately = new object[] { 0, 0, 0 };
        private const string OnFirstTry = "on first try";
        private const string AfterSeveralTries = "after several tries";

        private readonly StateMonitor<MyStateType, MyUpdateType> _monitor;
        private readonly Logger _log;

        private struct MyStateType
        {
            public int Counter { get; set; }
        }

        private struct MyUpdateType
        {
            public bool ShouldIncrement { get; set; }
        }

        public StateMonitorTest(ITestOutputHelper testOutput)
        {
            _log = Logs.ToMethod(line =>
            {
                try
                {
                    testOutput.WriteLine("{0}: {1}", DateTime.Now, line);
                }
                catch { }
            }).Logger("");
            var initialState = new MyStateType { Counter = 0 };
            _monitor = new StateMonitor<MyStateType, MyUpdateType>(initialState, MaybeUpdate, _log);
        }

        private static MyStateType? MaybeUpdate(MyStateType oldState, MyUpdateType update) =>
            update.ShouldIncrement ? new MyStateType { Counter = oldState.Counter + 1 } : (MyStateType?)null;

        [Fact]
        public void CanGetAndUpdateState()
        {
            Assert.Equal(new MyStateType { Counter = 0 }, _monitor.Current);

            Assert.True(_monitor.Update(new MyUpdateType { ShouldIncrement = true }, out var state1));
            Assert.Equal(new MyStateType { Counter = 1 }, state1);
            Assert.Equal(_monitor.Current, state1);

            Assert.False(_monitor.Update(new MyUpdateType { ShouldIncrement = false }, out var state2));
            Assert.Equal(state1, state2);
            Assert.Equal(_monitor.Current, state2);
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(1, 1, 50, 200)]
        [InlineData(3, 3, 40, 200)]
        public void WaitForSucceeds(int targetState, int numberOfUpdates, int updateDelayMs, int timeoutMs)
        {
            StartUpdating(numberOfUpdates, TimeSpan.FromMilliseconds(updateDelayMs));
            var result = _monitor.WaitFor(state => state.Counter == targetState, TimeSpan.FromMilliseconds(timeoutMs));
            Assert.NotNull(result);
            Assert.Equal(targetState, result.Value.Counter);
            Assert.Equal(targetState, _monitor.Current.Counter);
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(1, 1, 50, 200)]
        [InlineData(3, 3, 40, 200)]
        public async void WaitForAsyncSucceeds(int targetState, int numberOfUpdates, int updateDelayMs, int timeoutMs)
        {
            StartUpdating(numberOfUpdates, TimeSpan.FromMilliseconds(updateDelayMs));
            var result = await _monitor.WaitForAsync(state => state.Counter == targetState, TimeSpan.FromMilliseconds(timeoutMs));
            Assert.NotNull(result);
            Assert.Equal(targetState, result.Value.Counter);
            Assert.Equal(targetState, _monitor.Current.Counter);
        }

        [Fact]
        public void WaitForTimesOut()
        {
            StartUpdating(10, TimeSpan.FromMilliseconds(50));
            var result = _monitor.WaitFor(state => state.Counter == 10, TimeSpan.FromMilliseconds(200));
            Assert.Null(result);
        }

        [Fact]
        public async void WaitForAsyncTimesOut()
        {
            StartUpdating(10, TimeSpan.FromMilliseconds(50));
            var result = await _monitor.WaitForAsync(state => state.Counter == 10, TimeSpan.FromMilliseconds(200));
            Assert.Null(result);
        }

        private void StartUpdating(int numberOfUpdates, TimeSpan updateDelay)
        {
            if (numberOfUpdates > 0)
            {
                new Thread(() =>
                {
                    for (int i = 0; i < numberOfUpdates; i++)
                    {
                        Thread.Sleep(updateDelay);
                        _monitor.Update(new MyUpdateType { ShouldIncrement = true }, out _);
                    }
                }).Start();
            }
        }
    }
}
