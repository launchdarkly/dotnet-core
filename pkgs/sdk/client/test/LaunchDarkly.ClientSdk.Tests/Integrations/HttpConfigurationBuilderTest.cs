using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Logging;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    public class HttpConfigurationBuilderTest
    {
        private static string mobileKey = "mobile-key";
        private static ApplicationInfo applicationInfo => new ApplicationInfo("mockId", "mockName", "mockVersion", "mockVersionName");

        private readonly BuilderBehavior.BuildTester<HttpConfigurationBuilder, HttpConfiguration> _tester =
            BuilderBehavior.For(() => Components.HttpConfiguration(),
                b => b.CreateHttpConfiguration(mobileKey, applicationInfo));

        [Fact]
        public void ConnectTimeout()
        {
            var prop = _tester.Property(c => c.ConnectTimeout, (b, v) => b.ConnectTimeout(v));
            prop.AssertDefault(HttpConfigurationBuilder.DefaultConnectTimeout);
            prop.AssertCanSet(TimeSpan.FromSeconds(7));
        }

        [Fact]
        public void CustomHeaders()
        {
            var config = Components.HttpConfiguration()
                .CustomHeader("header1", "value1")
                .CustomHeader("header2", "value2")
                .CreateHttpConfiguration(mobileKey, applicationInfo);
            Assert.Equal("value1", HeadersAsMap(config.DefaultHeaders)["header1"]);
            Assert.Equal("value2", HeadersAsMap(config.DefaultHeaders)["header2"]);
        }

        [Fact]
        public void MessageHandler()
        {
            var prop = _tester.Property(c => c.MessageHandler, (b, v) => b.MessageHandler(v));
            // Can't test the default here because the default is platform-dependent.
            prop.AssertCanSet(new HttpClientHandler());
        }

        [Fact]
        public void MobileKeyHeader()
        {
            var config = Components.HttpConfiguration().CreateHttpConfiguration(mobileKey, applicationInfo);
            Assert.Equal(mobileKey, HeadersAsMap(config.DefaultHeaders)["authorization"]);
        }

        [Fact]
        public void ResponseStartTimeout()
        {
            var value = TimeSpan.FromMilliseconds(789);
            var prop = _tester.Property(c => c.ResponseStartTimeout, (b, v) => b.ResponseStartTimeout(v));
            prop.AssertDefault(HttpConfigurationBuilder.DefaultResponseStartTimeout);
            prop.AssertCanSet(value);

            var config = Components.HttpConfiguration().ResponseStartTimeout(value)
                .CreateHttpConfiguration(mobileKey, applicationInfo);
            using (var client = config.NewHttpClient())
            {
                Assert.Equal(value, client.Timeout);
            }
        }

        [Fact]
        public void UseReport()
        {
            var prop = _tester.Property(c => c.UseReport, (b, v) => b.UseReport(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
        }

        [Fact]
        public void UserAgentHeader()
        {
            var config = Components.HttpConfiguration().CreateHttpConfiguration(mobileKey, applicationInfo);
            Assert.Equal("DotnetClientSide/" + AssemblyVersions.GetAssemblyVersionStringForType(typeof(LdClient)),
                HeadersAsMap(config.DefaultHeaders)["user-agent"]); // not configurable
        }

        [Fact]
        public void WrapperDefaultNone()
        {
            var config = Components.HttpConfiguration().CreateHttpConfiguration(mobileKey, applicationInfo);
            Assert.False(HeadersAsMap(config.DefaultHeaders).ContainsKey("x-launchdarkly-wrapper"));
        }

        [Fact]
        public void WrapperNameOnly()
        {
            var config = Components.HttpConfiguration().Wrapper("w", null)
                .CreateHttpConfiguration(mobileKey, applicationInfo);
            Assert.Equal("w", HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-wrapper"]);
        }

        [Fact]
        public void WrapperNameAndVersion()
        {
            var config = Components.HttpConfiguration().Wrapper("w", "1.0")
                .CreateHttpConfiguration(mobileKey, applicationInfo);
            Assert.Equal("w/1.0", HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-wrapper"]);
        }

        [Fact]
        public void ApplicationTagsHeader()
        {
            var config = Components.HttpConfiguration().CreateHttpConfiguration(mobileKey, applicationInfo);
            Assert.Equal("application-id/mockId application-name/mockName application-version/mockVersion application-version-name/mockVersionName",
                HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-tags"]);
        }

        private static Dictionary<string, string> HeadersAsMap(IEnumerable<KeyValuePair<string, string>> headers)
        {
            return headers.ToDictionary(kv => kv.Key.ToLower(), kv => kv.Value);
        }
    }
}
