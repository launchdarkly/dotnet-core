using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    public class ConfigurationTest : BaseTest
    {
        private readonly BuilderBehavior.BuildTester<ConfigurationBuilder, Configuration> _tester =
            BuilderBehavior.For(() => Configuration.Builder(mobileKey, ConfigurationBuilder.AutoEnvAttributes.Disabled),
                    b => b.Build())
                .WithCopyConstructor(c => Configuration.Builder(c));

        const string mobileKey = "any-key";

        public ConfigurationTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        [Fact]
        public void DefaultSetsKey()
        {
            var config = Configuration.Default(mobileKey, ConfigurationBuilder.AutoEnvAttributes.Disabled);
            Assert.Equal(mobileKey, config.MobileKey);
        }

        [Fact]
        public void BuilderSetsKey()
        {
            var config = Configuration.Builder(mobileKey, ConfigurationBuilder.AutoEnvAttributes.Disabled).Build();
            Assert.Equal(mobileKey, config.MobileKey);
        }

        [Fact]
        public void ApplicationInfo()
        {
            var prop = _tester.Property(c => c.ApplicationInfo, (b, v) => b.ApplicationInfo(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(new ApplicationInfoBuilder());
        }

        [Fact]
        public void DataSource()
        {
            var prop = _tester.Property(c => c.DataSource, (b, v) => b.DataSource(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(new ComponentsImpl.NullDataSourceFactory());
        }

        [Fact]
        public void DiagnosticOptOut()
        {
            var prop = _tester.Property(c => c.DiagnosticOptOut, (b, v) => b.DiagnosticOptOut(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
        }

        [Fact]
        public void EnableBackgroundUpdating()
        {
            var prop = _tester.Property(c => c.EnableBackgroundUpdating, (b, v) => b.EnableBackgroundUpdating(v));
            prop.AssertDefault(true);
            prop.AssertCanSet(false);
        }

        [Fact]
        public void EvaluationReasons()
        {
            var prop = _tester.Property(c => c.EvaluationReasons, (b, v) => b.EvaluationReasons(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
        }

        [Fact]
        public void Events()
        {
            var prop = _tester.Property(c => c.Events, (b, v) => b.Events(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(new ComponentsImpl.NullEventProcessorFactory());
        }

        [Fact]
        public void Http()
        {
            var prop = _tester.Property(c => c.HttpConfigurationBuilder, (b, v) => b.Http(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(Components.HttpConfiguration());
        }

        [Fact]
        public void Logging()
        {
            var prop = _tester.Property(c => c.LoggingConfigurationBuilder, (b, v) => b.Logging(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(Components.Logging(Logs.ToWriter(Console.Out)));
        }

        [Fact]
        public void LoggingAdapterShortcut()
        {
            var adapter = Logs.ToWriter(Console.Out);
            var config = Configuration.Builder("key", ConfigurationBuilder.AutoEnvAttributes.Disabled).Logging(adapter)
                .Build();
            var logConfig = config.LoggingConfigurationBuilder.CreateLoggingConfiguration();
            Assert.Same(adapter, logConfig.LogAdapter);
        }

        [Fact]
        public void MobileKey()
        {
            var prop = _tester.Property(c => c.MobileKey, (b, v) => b.MobileKey(v));
            prop.AssertCanSet("other-key");
        }

        [Fact]
        public void Offline()
        {
            var prop = _tester.Property(c => c.Offline, (b, v) => b.Offline(v));
            prop.AssertDefault(false);
            prop.AssertCanSet(true);
        }

        [Fact]
        public void Persistence()
        {
            var prop = _tester.Property(c => c.PersistenceConfigurationBuilder, (b, v) => b.Persistence(v));
            prop.AssertDefault(null);
            prop.AssertCanSet(Components.Persistence().MaxCachedContexts(2));
        }

        [Fact]
        public void MobileKeyCannotBeNull()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Configuration.Default(null, ConfigurationBuilder.AutoEnvAttributes.Disabled));
        }

        [Fact]
        public void MobileKeyCannotBeEmpty()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Configuration.Default("", ConfigurationBuilder.AutoEnvAttributes.Disabled));
        }
    }
}
