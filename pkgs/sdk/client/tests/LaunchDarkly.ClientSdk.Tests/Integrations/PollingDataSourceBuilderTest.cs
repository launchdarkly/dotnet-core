using System;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    public class PollingDataSourceBuilderTest
    {
        private readonly BuilderBehavior.InternalStateTester<PollingDataSourceBuilder> _tester =
            BuilderBehavior.For(Components.PollingDataSource);

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
    }
}
