using System;
using LaunchDarkly.Logging;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Sdk;
using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Server
{
    public static class AssertHelpers
    {
        public static void DataSetsEqual(FullDataSet<ItemDescriptor> expected, FullDataSet<ItemDescriptor> actual) =>
            AssertJsonEqual(TestUtils.DataSetAsJson(expected), TestUtils.DataSetAsJson(actual));

        public static void DataItemsEqual(DataKind kind, ItemDescriptor expected, ItemDescriptor actual)
        {
            AssertJsonEqual(kind.Serialize(expected), kind.Serialize(actual));
            Assert.Equal(expected.Version, actual.Version);
        }

        public static void LogMessageRegex(LogCapture logCapture, bool shouldHave, LogLevel level, string pattern)
        {
            if (logCapture.HasMessageWithRegex(level, pattern) != shouldHave)
            {
                ThrowLogMatchException(logCapture, shouldHave, level, pattern, true);
            }
        }

        public static void LogMessageText(LogCapture logCapture, bool shouldHave, LogLevel level, string text)
        {
            if (logCapture.HasMessageWithText(level, text) != shouldHave)
            {
                ThrowLogMatchException(logCapture, shouldHave, level, text, true);
            }
        }

        private static void ThrowLogMatchException(LogCapture logCapture, bool shouldHave, LogLevel level, string text,
            bool isRegex) =>
            throw new AssertActualExpectedException(shouldHave, !shouldHave,
                string.Format("Expected log {0} the {1} \"{2}\" at level {3}\n\nActual log output follows:\n{4}",
                    shouldHave ? "to have" : "not to have",
                    isRegex ? "pattern" : "exact message",
                    text,
                    level,
                    logCapture.ToString()));

        /// <summary>
        /// Expect that the given sink will receive an event that passes the provided predicate within the specified
        /// timeout.
        ///
        /// The total time for the execution of this method may be greater than the timeout, because its implementation
        /// depends on a function which itself has a timeout.
        /// </summary>
        /// <param name="sink">the sink to check events from</param>
        /// <param name="predicate">the predicate to run against events</param>
        /// <param name="message">the message to show if the test fails</param>
        /// <param name="timeout">the overall timeout</param>
        /// <typeparam name="T">the type of the sink and predicate</typeparam>
        public static void ExpectPredicate<T>(EventSink<T> sink, Predicate<T> predicate, string message,
            TimeSpan timeout)
        {
            while (true)
            {
                var startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                var value = sink.ExpectValue(timeout);

                if (predicate(value))
                {
                    break;
                }

                if (!(DateTimeOffset.Now.ToUnixTimeMilliseconds() - startTime > timeout.TotalMilliseconds)) continue;

                // XUnit 2.5+ adds Assert.Fail.
                Assert.True(false, message);
                return;
            }
        }

        /// <summary>
        /// Expect that the given sink will receive a value that matches the expected JSON within the specified timeout.
        /// This handles race conditions where multiple events may be received (e.g., from file watcher triggers)
        /// by continuing to check events until one matches or the timeout expires.
        /// </summary>
        /// <param name="sink">the sink to check events from</param>
        /// <param name="expectedJson">the expected JSON string</param>
        /// <param name="actualJsonFunc">function to convert the received value to JSON for comparison</param>
        /// <param name="timeout">the overall timeout</param>
        /// <typeparam name="T">the type of the sink</typeparam>
        /// <returns>the value that matched the expected JSON</returns>
        public static T ExpectJsonValue<T>(EventSink<T> sink, string expectedJson, Func<T, string> actualJsonFunc,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (true)
            {
                var remaining = deadline - DateTime.UtcNow;
                Assert.True(remaining > TimeSpan.Zero, "Timed out waiting for expected JSON value");

                var candidate = sink.ExpectValue(remaining);

                try
                {
                    AssertJsonEqual(expectedJson, actualJsonFunc(candidate));
                    return candidate;
                }
                catch (XunitException)
                {
                    // This was an intermediate event that doesn't match; keep waiting for the correct one
                }
            }
        }
    }
}
