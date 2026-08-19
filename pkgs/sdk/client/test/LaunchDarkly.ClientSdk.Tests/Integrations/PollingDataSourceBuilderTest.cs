using System;
using System.Reflection;
using LaunchDarkly.Sdk.Client.Internal.DataSources;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    public class PollingDataSourceBuilderTest
    {
        private readonly BuilderBehavior.InternalStateTester<PollingDataSourceBuilder> _tester =
            BuilderBehavior.For(Components.PollingDataSource);

        private static TimeSpan GetPollingInterval(PollingDataSource dataSource) =>
            (TimeSpan)typeof(PollingDataSource)
                .GetField("_pollingInterval", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(dataSource);

        [Fact]
        public void BackgroundPollInterval()
        {
            var prop = _tester.Property(b => b._backgroundPollInterval, (b, v) => b.BackgroundPollInterval(v));
            prop.AssertDefault(Configuration.DefaultBackgroundPollInterval);
            prop.AssertCanSet(TimeSpan.FromMinutes(90));
            prop.AssertSetIsChangedTo(TimeSpan.FromMilliseconds(222), Configuration.MinimumBackgroundPollInterval);
        }


        [Fact]
        public void PollInterval()
        {
            var prop = _tester.Property(b => b._pollInterval, (b, v) => b.PollInterval(v));
            prop.AssertDefault(PollingDataSourceBuilder.DefaultPollInterval);
            prop.AssertCanSet(TimeSpan.FromMinutes(7));
            prop.AssertSetIsChangedTo(
                PollingDataSourceBuilder.DefaultPollInterval.Subtract(TimeSpan.FromMilliseconds(1)),
                PollingDataSourceBuilder.DefaultPollInterval);
        }

        [Fact]
        public void BuildUsesPollIntervalWhenForeground()
        {
            var builder = Components.PollingDataSource()
                .PollInterval(TimeSpan.FromMinutes(10))
                .BackgroundPollInterval(TimeSpan.FromMinutes(30));

            var dataSource = (PollingDataSource)builder.Build(TestUtil.SimpleContext.WithInBackground(false));

            Assert.Equal(TimeSpan.FromMinutes(10), GetPollingInterval(dataSource));
        }

        [Fact]
        public void BuildUsesBackgroundPollIntervalWhenInBackground()
        {
            var builder = Components.PollingDataSource()
                .PollInterval(TimeSpan.FromMinutes(10))
                .BackgroundPollInterval(TimeSpan.FromMinutes(30));

            var dataSource = (PollingDataSource)builder.Build(TestUtil.SimpleContext.WithInBackground(true));

            Assert.Equal(TimeSpan.FromMinutes(30), GetPollingInterval(dataSource));
        }
    }
}
