using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

using static LaunchDarkly.TestHelpers.Assertions;

namespace LaunchDarkly.Sdk.Internal.Concurrent
{
    public class TaskExecutorTest
    {
        private static readonly object MyEventSender = "this is the sender";

        private readonly TaskExecutor executor;
        private readonly LogCapture logCapture;
        private readonly Logger testLogger;
        private event EventHandler<string> myEvent;

        public TaskExecutorTest(ITestOutputHelper testOutput)
        {
            logCapture = Logs.Capture();
            testLogger = Logs.ToMultiple(
                logCapture,
                Logs.ToMethod(testOutput.WriteLine)
                ).Logger("");
            executor = new TaskExecutor(MyEventSender, testLogger);
        }

        [Fact]
        public void SendsEvent()
        {
            var values1 = new EventSink<string>();
            var values2 = new EventSink<string>();
            myEvent += values1.Add;
            myEvent += values2.Add;

            executor.ScheduleEvent("hello", myEvent);

            Assert.Equal("hello", values1.ExpectValue());
            Assert.Equal("hello", values2.ExpectValue());
        }

        [Fact]
        public void PassesConfiguredEventSenderToEventHandler()
        {
            var gotSender = new EventSink<object>();
            myEvent += (sender, args) => gotSender.Enqueue(sender);

            executor.ScheduleEvent("hello", myEvent);

            Assert.Equal(MyEventSender, gotSender.ExpectValue());
        }

        [Fact]
        public void ExceptionFromEventHandlerIsLoggedAndDoesNotStopOtherHandlers()
        {
            var values1 = new EventSink<string>();
            myEvent += (sender, args) => throw new Exception("sorry");
            myEvent += values1.Add;

            executor.ScheduleEvent("hello", myEvent);

            Assert.Equal("hello", values1.ExpectValue());

            AssertEventually(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20), () =>
                logCapture.HasMessageWithText(LogLevel.Error, "Unexpected exception from event handler for String: System.Exception: sorry") &&
                logCapture.HasMessageWithRegex(LogLevel.Debug, "at LaunchDarkly.Sdk.Internal.Concurrent.TaskExecutorTest"));
        }

        [Fact]
        public void CanUseCustomEventDispatcher()
        {
            var actions = new EventSink<Action>();
            var customExecutor = new TaskExecutor(MyEventSender, actions.Enqueue, testLogger);

            var values1 = new EventSink<string>();
            var values2 = new EventSink<string>();
            myEvent += values1.Add;
            myEvent += values2.Add;

            customExecutor.ScheduleEvent("hello", myEvent);

            values1.ExpectNoValue();
            values2.ExpectNoValue();

            var action1 = actions.ExpectValue();
            var action2 = actions.ExpectValue();
            actions.ExpectNoValue();

            action1();
            action2();
            Assert.Equal("hello", values1.ExpectValue());
            Assert.Equal("hello", values2.ExpectValue());
        }

        [Fact]
        public void RepeatingTask()
        {
            var values = new BlockingCollection<int>();
            var testGate = new EventWaitHandle(false, EventResetMode.AutoReset);
            var nextValue = 1;
            var canceller = executor.StartRepeatingTask(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), async () =>
            {
                testGate.WaitOne();
                values.Add(nextValue++);
                await Task.FromResult(true); // an arbitrary await just to make this function async
            });

            testGate.Set();
            Assert.True(values.TryTake(out var value1, TimeSpan.FromSeconds(2)));
            Assert.Equal(1, value1);

            testGate.Set();
            Assert.True(values.TryTake(out var value2, TimeSpan.FromSeconds(2)));
            Assert.Equal(2, value2);

            canceller.Cancel();
            testGate.Set();
            Assert.False(values.TryTake(out _, TimeSpan.FromMilliseconds(200)));
        }

        [Fact]
        public void ExceptionFromRepeatingTaskIsLoggedAndDoesNotStopTask()
        {
            var values = new BlockingCollection<int>();
            var testGate = new EventWaitHandle(false, EventResetMode.AutoReset);
            var nextValue = 1;
#pragma warning disable 1998
            var canceller = executor.StartRepeatingTask(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), async () =>
#pragma warning restore 1998
            {
                testGate.WaitOne();
                var valueWas = nextValue++;
                if (valueWas == 1)
                {
                    throw new Exception("sorry");
                }
                else
                {
                    values.Add(valueWas++);
                }
            });

            testGate.Set();
            Assert.False(values.TryTake(out _, TimeSpan.FromMilliseconds(100)));

            AssertEventually(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20), () =>
                logCapture.HasMessageWithText(LogLevel.Error, "Unexpected exception from repeating task: System.Exception: sorry") &&
                logCapture.HasMessageWithRegex(LogLevel.Debug, "at LaunchDarkly.Sdk.Internal.Concurrent.TaskExecutorTest"));

            testGate.Set();
            Assert.True(values.TryTake(out var value2, TimeSpan.FromSeconds(2)));
            Assert.Equal(2, value2);

            canceller.Cancel();
            testGate.Set();
        }
    }
}
