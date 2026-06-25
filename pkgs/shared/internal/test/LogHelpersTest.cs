using System;
using LaunchDarkly.Logging;
using Xunit;

namespace LaunchDarkly.Sdk.Internal
{
    public class LogHelpersTest
    {
        [Fact]
        public void LogException()
        {
            var logCapture = Logs.Capture();
            var logger = logCapture.Logger("logname");
            try
            {
                throw new ArgumentException("sorry");
            }
            catch (Exception e)
            {
                LogHelpers.LogException(logger, "Problem", e);
            }
            Assert.Equal(2, logCapture.GetMessages().Count);
            Assert.True(logCapture.HasMessageWithText(LogLevel.Error,
                "Problem: System.ArgumentException: sorry"), logCapture.ToString());
            Assert.True(logCapture.HasMessageWithRegex(LogLevel.Debug,
                "at LaunchDarkly\\.Sdk\\.Internal\\.LogHelpersTest\\.LogException"), logCapture.ToString());
        }
    }
}
