
using System;
using LaunchDarkly.Logging;

namespace LaunchDarkly.Sdk.Internal.Http
{
    /// <summary>
    /// Helper methods to provide standardized HTTP error-handling behavior in the SDKs.
    /// </summary>
    public static class HttpErrors
    {
        /// <summary>
        /// Returns true if this type of error could be expected to eventually resolve itself,
        /// or false if it indicates a configuration problem or client logic error such that the
        /// client should give up on making any further requests.
        /// </summary>
        /// <param name="status">a status code</param>
        /// <returns>true if retrying is appropriate</returns>
        public static bool IsRecoverable(int status)
        {
            if (status >= 400 && status <= 499)
            {
                return (status == 400) || (status == 408) || (status == 429);
            }
            return true;
        }

        public static string ErrorMessage(int status, string context, string recoverableMessage) =>
            string.Format("{0} for {1} - {2}",
                ErrorMessageBase(status),
                context,
                IsRecoverable(status) ? recoverableMessage : "giving up permanently"
                );

        public static string ErrorMessageBase(int status) =>
            string.Format("HTTP error {0}{1}",
                status,
               (status == 401 || status == 403) ? " (invalid SDK key)" : "");

        /// <summary>
        /// Classifies an HTTP status code by whether it is likely to clear on its own.
        /// </summary>
        /// <remarks>
        /// 400, 408 and 429 are <see cref="FailureClass.Normal"/> because they are commonly
        /// transient; every other 4xx is <see cref="FailureClass.Unexpected"/> because it usually
        /// reflects a bad key or a misdirected request. 5xx is <see cref="FailureClass.Normal"/>.
        /// A status outside the error range is <see cref="FailureClass.Normal"/>, so a caller that
        /// passes 0 for "no HTTP response" gets the same answer as a transient error.
        /// </remarks>
        /// <param name="status">an HTTP status code, or 0 if there was no response</param>
        /// <returns>the classification</returns>
        public static FailureClass ClassifyHttpFailure(int status)
        {
            if (status == 400 || status == 408 || status == 429)
            {
                return FailureClass.Normal;
            }
            if (status >= 500)
            {
                return FailureClass.Normal;
            }
            if (status >= 400 && status < 500)
            {
                return FailureClass.Unexpected;
            }
            return FailureClass.Normal;
        }

        /// <summary>
        /// Classifies a transport-level failure by whether it is likely to clear on its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Always <see cref="FailureClass.Normal"/>. The spec singles out certificate and TLS
        /// trust failures as <see cref="FailureClass.Unexpected"/>, but .NET provides no way to
        /// recognise them with confidence: a rejected certificate surfaces as
        /// <c>AuthenticationException</c>, the same type raised for protocol and cipher mismatches
        /// and for a connection dying mid-handshake, and the shape varies by target framework and
        /// message handler. Nothing on the HTTP path reliably produces a certificate-specific type.
        /// </para>
        /// <para>
        /// Guessing is worse than not classifying, because the costs are asymmetric. Misreading a
        /// transient failure as unexpected slows reconnection to minutes and, for polling, requires
        /// two consecutive successes to recover -- so a brief TLS hiccup during a load-balancer
        /// rollout would leave a whole fleet on stale data with no action available to the customer.
        /// Misreading a real certificate problem as transient only means retrying at the ordinary
        /// cadence, which is bounded by the configured ceiling and clears as soon as someone fixes
        /// the certificate. So an unrecognised failure resolves to the retryable side, which is how
        /// the spec's own table is arranged: every transport row is normal apart from the one this
        /// SDK cannot detect.
        /// </para>
        /// <para>
        /// If a framework version exposes a certificate-specific signal, this is where to narrow
        /// it. Matching on exception message text is the alternative, and is deliberately not done.
        /// </para>
        /// </remarks>
        /// <param name="e">the exception, or null; not inspected</param>
        /// <returns><see cref="FailureClass.Normal"/></returns>
        public static FailureClass ClassifyTransportFailure(Exception e) => FailureClass.Normal;

        /// <summary>
        /// Classifies an HTTP status code and logs it at a level matching the classification.
        /// </summary>
        /// <param name="logger">the logger</param>
        /// <param name="status">an HTTP status code</param>
        /// <param name="errorContext">what was being attempted, e.g. "streaming connection"</param>
        /// <param name="willRetryMessage">how the retry will proceed, e.g. "will retry"</param>
        /// <returns>the classification</returns>
        public static FailureClass ClassifyAndLogHttpFailure(
            Logger logger,
            int status,
            string errorContext,
            string willRetryMessage
            )
        {
            var failureClass = ClassifyHttpFailure(status);
            LogClassified(logger, failureClass, ErrorMessageBase(status), errorContext,
                willRetryMessage);
            return failureClass;
        }

        /// <summary>
        /// Classifies a transport-level failure and logs it at a level matching the classification.
        /// </summary>
        /// <param name="logger">the logger</param>
        /// <param name="e">the exception</param>
        /// <param name="errorContext">what was being attempted, e.g. "streaming connection"</param>
        /// <param name="willRetryMessage">how the retry will proceed, e.g. "will retry"</param>
        /// <returns>the classification</returns>
        public static FailureClass ClassifyAndLogTransportFailure(
            Logger logger,
            Exception e,
            string errorContext,
            string willRetryMessage
            )
        {
            var failureClass = ClassifyTransportFailure(e);
            LogClassified(logger, failureClass, LogValues.ExceptionSummary(e).ToString(),
                errorContext, willRetryMessage);
            return failureClass;
        }

        private static void LogClassified(Logger logger, FailureClass failureClass, string errorDesc,
            string errorContext, string willRetryMessage)
        {
            // Unexpected is logged at Error because it usually needs someone to act; Normal is
            // logged at Warn because it is expected to clear on its own.
            if (failureClass == FailureClass.Unexpected)
            {
                logger.Error("Error {0} ({1}): {2}", errorContext, willRetryMessage, errorDesc);
            }
            else
            {
                logger.Warn("Error {0} ({1}): {2}", errorContext, willRetryMessage, errorDesc);
            }
        }

    }
}
