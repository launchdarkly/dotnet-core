using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class HttpConfigurationBuilderTest
    {
        private static readonly LdClientContext basicConfig = new LdClientContext("sdk-key");

        private readonly BuilderBehavior.BuildTester<HttpConfigurationBuilder, HttpConfiguration> _tester =
            BuilderBehavior.For(() => Components.HttpConfiguration(),
                b => b.Build(basicConfig));

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
                .Build(basicConfig);
            Assert.Equal("value1", HeadersAsMap(config.DefaultHeaders)["header1"]);
            Assert.Equal("value2", HeadersAsMap(config.DefaultHeaders)["header2"]);
        }

        [Fact]
        public void ApplicationInfo()
        {
            var config = Components.HttpConfiguration().Build(basicConfig
                .WithApplicationInfo(Components.ApplicationInfo()
                    .ApplicationId("my-app")
                    .ApplicationVersion("my-version").Build()));

            Assert.Equal("application-id/my-app application-version/my-version",
                HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-tags"]);

            var config2 = Components.HttpConfiguration().Build(basicConfig
                .WithApplicationInfo(Components.ApplicationInfo()
                    .ApplicationVersion("my-version")
                    .ApplicationName("MY_NAME")
                    .ApplicationVersionName("my-friendly-version")
                    .ApplicationId("my-app").Build()));

            Assert.Equal(
                "application-id/my-app application-name/MY_NAME application-version/my-version" +
                " application-version-name/my-friendly-version",
                HeadersAsMap(config2.DefaultHeaders)["x-launchdarkly-tags"]);
        }

        [Fact]
        public void MessageHandler()
        {
            var prop = _tester.Property(c => c.MessageHandler, (b, v) => b.MessageHandler(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(new HttpClientHandler());
        }

        [Fact]
        public void ReadTimeout()
        {
            var prop = _tester.Property(c => c.ReadTimeout, (b, v) => b.ReadTimeout(v));
            prop.AssertDefault(HttpConfigurationBuilder.DefaultReadTimeout);
            prop.AssertCanSet(TimeSpan.FromSeconds(7));
        }

        [Fact]
        public void ResponseStartTimeout()
        {
            var value = TimeSpan.FromMilliseconds(789);
            var prop = _tester.Property(c => c.ResponseStartTimeout, (b, v) => b.ResponseStartTimeout(v));
            prop.AssertDefault(HttpConfigurationBuilder.DefaultResponseStartTimeout);
            prop.AssertCanSet(value);

            var config = Components.HttpConfiguration().ResponseStartTimeout(value)
                .Build(basicConfig);
            using (var client = config.NewHttpClient())
            {
                Assert.Equal(value, client.Timeout);
            }
        }

        [Fact]
        public void SdkKeyHeader()
        {
            var config = Components.HttpConfiguration().Build(basicConfig);
            Assert.Equal(basicConfig.SdkKey, HeadersAsMap(config.DefaultHeaders)["authorization"]);
        }

        [Fact]
        public void UserAgentHeader()
        {
            var config = Components.HttpConfiguration().Build(basicConfig);
            Assert.Equal("DotNetClient/" + AssemblyVersions.GetAssemblyVersionStringForType(typeof(LdClient)),
                HeadersAsMap(config.DefaultHeaders)["user-agent"]); // not configurable
        }

        [Fact]
        public void WrapperDefaultNone()
        {
            var config = Components.HttpConfiguration().Build(basicConfig);
            Assert.False(HeadersAsMap(config.DefaultHeaders).ContainsKey("x-launchdarkly-wrapper"));
        }

        [Fact]
        public void WrapperNameOnly()
        {
            var config = Components.HttpConfiguration().Wrapper("w", null)
                .Build(basicConfig);
            Assert.Equal("w", HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-wrapper"]);
        }

        [Fact]
        public void WrapperNameAndVersion()
        {
            var config = Components.HttpConfiguration().Wrapper("w", "1.0")
                .Build(basicConfig);
            Assert.Equal("w/1.0", HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-wrapper"]);
        }

        [Fact]
        public void WrapperInfoNameOnly()
        {
            var wrapperInfo = Components.WrapperInfo().Name("my-wrapper").Build();
            var config = Components.HttpConfiguration()
                .Build(basicConfig.WithWrapperInfo(wrapperInfo));
            Assert.Equal("my-wrapper", HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-wrapper"]);
        }

        [Fact]
        public void WrapperInfoNameAndVersion()
        {
            var wrapperInfo = Components.WrapperInfo().Name("my-wrapper").Version("3.14").Build();
            var config = Components.HttpConfiguration()
                .Build(basicConfig.WithWrapperInfo(wrapperInfo));
            Assert.Equal("my-wrapper/3.14", HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-wrapper"]);
        }

        [Fact]
        public void WrapperInfoOverwritesHttpConfiguration()
        {
            var wrapperInfo = Components.WrapperInfo().Name("my-wrapper").Version("3.14").Build();
            var config = Components.HttpConfiguration().Wrapper("w", "1.0")
                .Build(basicConfig.WithWrapperInfo(wrapperInfo));
            Assert.Equal("my-wrapper/3.14", HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-wrapper"]);
        }

        [Fact]
        public void InstanceIdHeaderIsPresentAndIsUuidV4()
        {
            var config = Components.HttpConfiguration().Build(basicConfig);
            var headers = HeadersAsMap(config.DefaultHeaders);
            Assert.True(headers.ContainsKey("x-launchdarkly-instance-id"),
                "X-LaunchDarkly-Instance-Id header must be set");

            var raw = headers["x-launchdarkly-instance-id"];
            Assert.True(Guid.TryParse(raw, out var parsed),
                $"instance id '{raw}' must be a parseable GUID");

            // The "M" (version) nibble of a v4 UUID is 0x4. In the canonical 8-4-4-4-12 form,
            // that is the first character of the third group.
            var groups = raw.Split('-');
            Assert.Equal(5, groups.Length);
            Assert.Equal('4', groups[2][0]);
        }

        [Fact]
        public void InstanceIdHeaderIsUniquePerSdkInstance()
        {
            // Each call to Build represents a new SDK instance; each must get its own GUID.
            var config1 = Components.HttpConfiguration().Build(basicConfig);
            var config2 = Components.HttpConfiguration().Build(basicConfig);
            var id1 = HeadersAsMap(config1.DefaultHeaders)["x-launchdarkly-instance-id"];
            var id2 = HeadersAsMap(config2.DefaultHeaders)["x-launchdarkly-instance-id"];
            Assert.False(string.IsNullOrEmpty(id1));
            Assert.False(string.IsNullOrEmpty(id2));
            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void InstanceIdHeaderIsStableAcrossBuildsOnSameBuilder()
        {
            // A single builder is reused by both Build() and DescribeConfiguration() against the
            // same SDK instance, so the generated GUID must be fixed at construction rather than
            // regenerated on each call.
            var builder = Components.HttpConfiguration();
            var id1 = HeadersAsMap(builder.Build(basicConfig).DefaultHeaders)["x-launchdarkly-instance-id"];
            var id2 = HeadersAsMap(builder.Build(basicConfig).DefaultHeaders)["x-launchdarkly-instance-id"];
            Assert.Equal(id1, id2);
        }

        [Fact]
        public void CustomHeaderCanOverrideInstanceIdHeader()
        {
            // Consistent with User-Agent / Authorization: a user-supplied custom header for the
            // same name takes precedence. This mirrors the behavior in other SDKs.
            var config = Components.HttpConfiguration()
                .CustomHeader("X-LaunchDarkly-Instance-Id", "custom-override")
                .Build(basicConfig);
            Assert.Equal("custom-override",
                HeadersAsMap(config.DefaultHeaders)["x-launchdarkly-instance-id"]);
        }

        private Dictionary<string, string> HeadersAsMap(IEnumerable<KeyValuePair<string, string>> headers)
        {
            return headers.ToDictionary(kv => kv.Key.ToLower(), kv => kv.Value);
        }
    }
}
