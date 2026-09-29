using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using LaunchDarkly.Logging;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Internal.Http
{
    public class HttpErrorsClassificationTest
    {
        private readonly LogCapture _logCapture;
        private readonly Logger _testLogger;

        public HttpErrorsClassificationTest(ITestOutputHelper testOutput)
        {
            _logCapture = Logs.Capture();
            _testLogger = Logs.ToMultiple(_logCapture, Logs.ToMethod(testOutput.WriteLine)).Logger("");
        }

        #region HTTP status classification

        [Theory]
        [InlineData(400)] // bad request -- commonly a transient upstream problem
        [InlineData(408)] // request timeout
        [InlineData(429)] // too many requests
        public void CommonlyTransient4xxIsNormal(int status) =>
            Assert.Equal(FailureClass.Normal, HttpErrors.ClassifyHttpFailure(status));

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(404)]
        [InlineData(418)]
        [InlineData(451)]
        public void Other4xxIsUnexpected(int status) =>
            Assert.Equal(FailureClass.Unexpected, HttpErrors.ClassifyHttpFailure(status));

        [Theory]
        [InlineData(500)]
        [InlineData(502)]
        [InlineData(503)]
        [InlineData(504)]
        [InlineData(599)]
        public void ServerErrorIsNormal(int status) =>
            Assert.Equal(FailureClass.Normal, HttpErrors.ClassifyHttpFailure(status));

        [Fact]
        public void NonErrorStatusIsNormal() =>
            Assert.Equal(FailureClass.Normal, HttpErrors.ClassifyHttpFailure(300));

        [Fact]
        public void ZeroStatusIsNormal() =>
            // 0 means "no HTTP response at all", so it must not be read as a 4xx.
            Assert.Equal(FailureClass.Normal, HttpErrors.ClassifyHttpFailure(0));

        #endregion

        #region Transport classification -- not certificate related

        [Fact]
        public void ConnectionRefusedIsNormal() =>
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(
                    new HttpRequestException("refused",
                        new SocketException((int)SocketError.ConnectionRefused))));

        [Fact]
        public void TimeoutIsNormal() =>
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(new TimeoutException("timed out")));

        [Fact]
        public void IOExceptionIsNormal() =>
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(new IOException("connection reset")));

        [Fact]
        public void NullExceptionIsNormal() =>
            Assert.Equal(FailureClass.Normal, HttpErrors.ClassifyTransportFailure(null));

        #endregion

        #region Transport classification -- TLS and certificates are deliberately Normal

        // .NET cannot recognise a certificate failure with confidence: the type a rejected
        // certificate produces is the same one raised for protocol and cipher mismatches, and it
        // varies by target framework. So every transport failure is Normal, including the shapes a
        // real certificate failure takes. These cases pin that on purpose -- misreading a transient
        // failure as unexpected freezes a healthy fleet, while misreading a certificate problem as
        // transient only costs retries at the ordinary cadence.

        [Fact]
        public void AuthenticationExceptionIsNormal() =>
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(
                    new AuthenticationException("The remote certificate is invalid")));

        [Fact]
        public void CryptographicExceptionIsNormal() =>
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(new CryptographicException("bad cert")));

        [Fact]
        public void RealCertificateFailureShapeOnNet8IsNormal() =>
            // Verified empirically against a self-signed-certificate server on net8.0.
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(
                    new HttpRequestException("The SSL connection could not be established",
                        new AuthenticationException(
                            "The remote certificate is invalid according to the validation " +
                            "procedure: RemoteCertificateNameMismatch"))));

        [Fact]
        public void NetFrameworkTrustFailureShapeIsNormal() =>
            // The shape .NET Framework is expected to produce. Unverified -- net462 needs a
            // Windows host -- but Normal either way, so the uncertainty costs nothing.
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(
                    new HttpRequestException("request failed",
                        new WebException("connection closed", WebExceptionStatus.TrustFailure))));

        [Fact]
        public void CertificateCauseInAnAggregateBranchIsNormal() =>
            // Exception.InnerException on an AggregateException exposes only branch 0, so a cause
            // in a later branch was previously invisible. Now moot: the answer is Normal anyway.
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(
                    new AggregateException(
                        new TimeoutException("first"),
                        new AuthenticationException("cert invalid"))));

        [Fact]
        public void BareHandshakeFailureIsNormal() =>
            // The case that motivated this: a handshake failure with no certificate involvement is
            // typically transient, and .NET gives it the same type as a certificate rejection.
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(
                    new AuthenticationException("Authentication failed because the remote party " +
                        "closed the transport stream")));

        [Fact]
        public void CertificateCauseSeveralLevelsDownIsNormal() =>
            Assert.Equal(FailureClass.Normal,
                HttpErrors.ClassifyTransportFailure(
                    new HttpRequestException("outer",
                        new IOException("middle",
                            new AuthenticationException("cert invalid")))));

        #endregion

        #region Classify and log

        [Fact]
        public void UnexpectedHttpFailureLogsAtError()
        {
            var result = HttpErrors.ClassifyAndLogHttpFailure(_testLogger, 401,
                "in stream connection", "will retry");

            Assert.Equal(FailureClass.Unexpected, result);
            Assert.Equal(LogLevel.Error, LastLine().Level);
        }

        [Fact]
        public void NormalHttpFailureLogsAtWarn()
        {
            var result = HttpErrors.ClassifyAndLogHttpFailure(_testLogger, 503,
                "in stream connection", "will retry");

            Assert.Equal(FailureClass.Normal, result);
            Assert.Equal(LogLevel.Warn, LastLine().Level);
        }

        [Fact]
        public void EvenACertificateFailureLogsTransportAtWarn()
        {
            // Follows from the classification: there is no transport failure this SDK reports at
            // Error, because it cannot identify one with confidence.
            var result = HttpErrors.ClassifyAndLogTransportFailure(_testLogger,
                new AuthenticationException("The remote certificate is invalid"),
                "in stream connection", "will retry");

            Assert.Equal(FailureClass.Normal, result);
            Assert.Equal(LogLevel.Warn, LastLine().Level);
        }

        [Fact]
        public void NormalTransportFailureLogsAtWarn()
        {
            var result = HttpErrors.ClassifyAndLogTransportFailure(_testLogger,
                new IOException("connection reset"), "in stream connection", "will retry");

            Assert.Equal(FailureClass.Normal, result);
            Assert.Equal(LogLevel.Warn, LastLine().Level);
        }

        [Fact]
        public void LoggedMessageUsesTheStandardShape()
        {
            HttpErrors.ClassifyAndLogHttpFailure(_testLogger, 500,
                "on polling request", "will retry at next scheduled poll interval");

            Assert.Equal(
                "Error on polling request (will retry at next scheduled poll interval): HTTP error 500",
                LastLine().Text);
        }

        [Fact]
        public void InvalidSdkKeyIsCalledOutInTheMessage()
        {
            HttpErrors.ClassifyAndLogHttpFailure(_testLogger, 401, "in stream connection",
                "will retry");

            Assert.Contains("(invalid SDK key)", LastLine().Text);
        }

        [Theory]
        [InlineData(401)]
        [InlineData(503)]
        public void LoggingVariantReturnsTheSameClassAsTheNonLoggingOne(int status) =>
            Assert.Equal(
                HttpErrors.ClassifyHttpFailure(status),
                HttpErrors.ClassifyAndLogHttpFailure(_testLogger, status, "ctx", "msg"));

        private LogCapture.Message LastLine()
        {
            var all = _logCapture.GetMessages();
            Assert.NotEmpty(all);
            return all[all.Count - 1];
        }

        #endregion
    }
}
