using System;
using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Internal
{
    public static class LogHelpers
    {
        /// <summary>
        /// Logs an exception using the standard pattern for the LaunchDarkly SDKs.
        /// </summary>
        /// <remarks>
        /// The exception summary is logged at Error level. Then the stacktrace is logged at
        /// Debug level, using lazy evaluation to avoid computing it if Debug loggins is disabled.
        /// </remarks>
        /// <param name="logger">the logger instance</param>
        /// <param name="message">a descriptive prefix ("Unexpected error" if null or empty)</param>
        /// <param name="e">the exception</param>
        public static void LogException(Logger logger, string message, Exception e)
        {
            logger.Error("{0}: {1}",
                string.IsNullOrEmpty(message) ? "Unexpected error" : message,
                LogValues.ExceptionSummary(e));
            logger.Debug(LogValues.ExceptionTrace(e));
        }
    }
}
