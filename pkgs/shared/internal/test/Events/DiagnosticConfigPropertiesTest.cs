using System;
using System.Collections;
using System.Net;
using LaunchDarkly.Sdk.Internal.Http;
using Xunit;

using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class DiagnosticConfigPropertiesTest : IDisposable
    {
        private readonly IDictionary _oldEnvVars;

        public DiagnosticConfigPropertiesTest()
        {
            _oldEnvVars = Environment.GetEnvironmentVariables();
        }

        public void Dispose()
        {
            foreach (var key in _oldEnvVars.Keys)
            {
                Environment.SetEnvironmentVariable(key.ToString(), _oldEnvVars[key]?.ToString());
            }
            foreach (var key in Environment.GetEnvironmentVariables().Keys)
            {
                if (!_oldEnvVars.Contains(key.ToString()))
                {
                    Environment.SetEnvironmentVariable(key.ToString(), null);
                }
            }
        }

        [Fact]
        public void WithEventProperties()
        {
            foreach (var customEventsBasUri in new bool[] { false, true })
            {
                foreach (var allAttributesPrivate in new bool[] { false, true })
                {
                    var eventsConfig = new EventsConfiguration
                    {
                        AllAttributesPrivate = allAttributesPrivate,
                        DiagnosticRecordingInterval = TimeSpan.FromMilliseconds(11111),
                        EventCapacity = 22222,
                        EventFlushInterval = TimeSpan.FromMilliseconds(33333)
                    };
                    string expected = LdValue.BuildObject()
                        .Add("allAttributesPrivate", allAttributesPrivate)
                        .Add("customEventsURI", customEventsBasUri)
                        .Add("diagnosticRecordingIntervalMillis", 11111)
                        .Add("eventsCapacity", 22222)
                        .Add("eventsFlushIntervalMillis", 33333)
                        .Build().ToJsonString();
                    string actual = LdValue.BuildObject().WithEventProperties(eventsConfig, customEventsBasUri)
                        .Build().ToJsonString();
                    AssertJsonEqual(expected, actual);
                }
            }
        }

        [Fact]
        public void WithHttpProperties()
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", null);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
            Environment.SetEnvironmentVariable("ALL_PROXY", null);

            string expected = LdValue.BuildObject()
                .Add("connectTimeoutMillis", 11111)
                .Add("socketTimeoutMillis", 22222)
                .Add("usingProxy", false)
                .Add("usingProxyAuthenticator", false)
                .Build().ToJsonString();
            var httpProps = HttpProperties.Default
                .WithConnectTimeout(TimeSpan.FromMilliseconds(11111))
                .WithReadTimeout(TimeSpan.FromMilliseconds(22222));
            string actual = LdValue.BuildObject().WithHttpProperties(httpProps).Build().ToJsonString();
            AssertJsonEqual(expected, actual);
        }

        [Fact]
        public void WithHttpPropertiesWithConfiguredProxy()
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", null);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
            Environment.SetEnvironmentVariable("ALL_PROXY", null);

            string expected = LdValue.BuildObject()
                .Add("connectTimeoutMillis", HttpProperties.DefaultConnectTimeout.TotalMilliseconds)
                .Add("socketTimeoutMillis", HttpProperties.DefaultReadTimeout.TotalMilliseconds)
                .Add("usingProxy", true)
                .Add("usingProxyAuthenticator", false)
                .Build().ToJsonString();
            var httpProps = HttpProperties.Default
                .WithProxy(new WebProxy("http://example"));
            string actual = LdValue.BuildObject().WithHttpProperties(httpProps).Build().ToJsonString();
            AssertJsonEqual(expected, actual);
        }

        [Fact]
        public void WithHttpPropertiesWithConfiguredProxyWithAuthentication()
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", null);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
            Environment.SetEnvironmentVariable("ALL_PROXY", null);

            string expected = LdValue.BuildObject()
                .Add("connectTimeoutMillis", HttpProperties.DefaultConnectTimeout.TotalMilliseconds)
                .Add("socketTimeoutMillis", HttpProperties.DefaultReadTimeout.TotalMilliseconds)
                .Add("usingProxy", true)
                .Add("usingProxyAuthenticator", true)
                .Build().ToJsonString();

            var credentials = new CredentialCache();
            credentials.Add(new Uri("http://example"), "Basic", new NetworkCredential("user", "pass"));
            var proxyWithAuth = new WebProxy(new Uri("http://example"));
            proxyWithAuth.Credentials = credentials;
            var httpProps = HttpProperties.Default
                .WithProxy(proxyWithAuth);

            string actual = LdValue.BuildObject().WithHttpProperties(httpProps).Build().ToJsonString();
            AssertJsonEqual(expected, actual);
        }

        [Fact]
        public void WithHttpPropertiesWithProxyFromEnvVar()
        {
            string expected = LdValue.BuildObject()
                .Add("connectTimeoutMillis", HttpProperties.DefaultConnectTimeout.TotalMilliseconds)
                .Add("socketTimeoutMillis", HttpProperties.DefaultReadTimeout.TotalMilliseconds)
                .Add("usingProxy", true)
                .Add("usingProxyAuthenticator", false)
                .Build().ToJsonString();
            var httpProps = HttpProperties.Default;

            Environment.SetEnvironmentVariable("HTTP_PROXY", "http://example");
            string actual1 = LdValue.BuildObject().WithHttpProperties(httpProps).Build().ToJsonString();
            AssertJsonEqual(expected, actual1);

            Environment.SetEnvironmentVariable("HTTP_PROXY", null);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://example");
            string actual2 = LdValue.BuildObject().WithHttpProperties(httpProps).Build().ToJsonString();
            AssertJsonEqual(expected, actual2);

            Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
            Environment.SetEnvironmentVariable("ALL_PROXY", "http://example");
            string actual3 = LdValue.BuildObject().WithHttpProperties(httpProps).Build().ToJsonString();
            AssertJsonEqual(expected, actual3);
        }

        [Fact]
        public void WithStartWaitTime()
        {
            string expected = LdValue.BuildObject().Add("startWaitMillis", 11111)
                .Build().ToJsonString();
            string actual = LdValue.BuildObject().WithStartWaitTime(TimeSpan.FromMilliseconds(11111))
                .Build().ToJsonString();
            AssertJsonEqual(expected, actual);
        }

        [Fact]
        public void WithStreamingProperties()
        {
            foreach (var customStreamingBasUri in new bool[] { false, true })
            {
                foreach (var customPollingBaseUri in new bool[] { false, true })
                {
                    string expected = LdValue.BuildObject()
                        .Add("streamingDisabled", false)
                        .Add("customBaseURI", customPollingBaseUri)
                        .Add("customStreamURI", customStreamingBasUri)
                        .Add("reconnectTimeMillis", 11111)
                        .Build().ToJsonString();
                    string actual = LdValue.BuildObject().WithStreamingProperties(
                        customStreamingBasUri,
                        customPollingBaseUri,
                        TimeSpan.FromMilliseconds(11111)
                        ).Build().ToJsonString();
                    AssertJsonEqual(expected, actual);
                }
            }
        }

        [Fact]
        public void WithPollingProperties()
        {
            foreach (var customPollingBaseUri in new bool[] { false, true })
            {
                string expected = LdValue.BuildObject()
                    .Add("streamingDisabled", true)
                    .Add("customBaseURI", customPollingBaseUri)
                    .Add("pollingIntervalMillis", 11111)
                    .Build().ToJsonString();
                string actual = LdValue.BuildObject().WithPollingProperties(
                    customPollingBaseUri,
                    TimeSpan.FromMilliseconds(11111)
                    ).Build().ToJsonString();
                AssertJsonEqual(expected, actual);
            }
        }
    }
}
