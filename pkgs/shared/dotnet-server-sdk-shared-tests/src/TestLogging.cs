using LaunchDarkly.Logging;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.SharedTests
{
    /// <summary>
    /// Provides integration between LaunchDarkly logging and Xunit test output.
    /// </summary>
    public class TestLogging
    {
        /// <summary>
        /// Allows LaunchDarkly log output to be captured in the Xunit test output buffer.
        /// </summary>
        /// <remarks>
        /// Xunit suppresses console output from tests, because tests can run in parallel and
        /// so their output could be interleaved and unreadable. Instead, it provides the
        /// <c>ITestOutputHelper</c> interface; any test class constructor that declares a
        /// parameter of this type will receive an instance of it, and output sent to the
        /// interface will be printed along with the test results if the test fails. Calling
        /// <c>TestLogging.TestOutputAdapter</c> converts the <c>ITestOutputHelper</c> into
        /// the type that is used for LaunchDarkly logging configuration.
        /// </remarks>
        /// <example>
        /// </example>
        /// <param name="testOutputHelper">an <c>ITestOutputHelper</c> provided by Xunit</param>
        /// <param name="prefix">optional text that will be prepended to each log line, to
        /// distinguish it from any other kind of output from the test</param>
        /// <returns>an <c>ILogAdapter</c> for use in an LaunchDarkly SDK</returns>
        public static ILogAdapter TestOutputAdapter(ITestOutputHelper testOutputHelper,
            string prefix = null) =>
            Logs.ToMethod(line => testOutputHelper.WriteLine((prefix ?? "") + line));
    }
}
