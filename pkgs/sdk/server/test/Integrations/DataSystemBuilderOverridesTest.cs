using Xunit;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    public class DataSystemBuilderOverridesTest
    {
        [Fact]
        public void OverrideSourceIsNullByDefault()
        {
            Assert.Null(Components.DataSystem().Default().Build().OverrideSource);
            Assert.Null(Components.DataSystem().Custom().Build().OverrideSource);
        }

        [Fact]
        public void OverridesSetsTheOverrideSource()
        {
            var source = new TestOverrideSource();
            var config = Components.DataSystem().Default().Overrides(source).Build();
            Assert.Same(source, config.OverrideSource);
            // The rest of the default configuration is unchanged.
            Assert.Single(config.Initializers);
            Assert.Equal(2, config.Synchronizers.Count);
        }

        [Fact]
        public void LaterCallReplacesTheOverrideSource()
        {
            var first = new TestOverrideSource();
            var second = new TestOverrideSource();
            var config = Components.DataSystem().Custom().Overrides(first).Overrides(second).Build();
            Assert.Same(second, config.OverrideSource);

            Assert.Null(Components.DataSystem().Custom().Overrides(first).Overrides(null).Build().OverrideSource);
        }
    }
}
