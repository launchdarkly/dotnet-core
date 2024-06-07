using System;
using LaunchDarkly.TestHelpers;
using Xunit;

namespace LaunchDarkly.Sdk.Client.Integrations
{
    public class StreamingDataSourceBuilderTest
    {
        private readonly BuilderBehavior.InternalStateTester<StreamingDataSourceBuilder> _tester =
            BuilderBehavior.For(Components.StreamingDataSource);

        [Fact]
        public void BackgroundPollInterval()
        {
            var prop = _tester.Property(b => b._backgroundPollInterval, (b, v) => b.BackgroundPollInterval(v));
            prop.AssertDefault(Configuration.DefaultBackgroundPollInterval);
            prop.AssertCanSet(TimeSpan.FromMinutes(90));
            prop.AssertSetIsChangedTo(TimeSpan.FromMilliseconds(222), Configuration.MinimumBackgroundPollInterval);
        }

        [Fact]
        public void InitialReconnectDelay()
        {
            var prop = _tester.Property(b => b._initialReconnectDelay, (b, v) => b.InitialReconnectDelay(v));
            prop.AssertDefault(StreamingDataSourceBuilder.DefaultInitialReconnectDelay);
            prop.AssertCanSet(TimeSpan.FromMilliseconds(222));
        }
    }
}
