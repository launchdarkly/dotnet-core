using Xunit;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    /// <summary>
    /// These tests are for the <see cref="LdClientContext"/>.  Since some of the behavior is platform dependent
    /// and the .NET Standard does not support some platform capabilities we want to test, this test class is
    /// in the Android tests package.
    /// </summary>
    public class LdClientContextTests
    {
        [Fact]
        public void TestMakeEnvironmentReporterUsesApplicationInfoWhenSet()
        {
            var configuration = Configuration.Builder("aKey", ConfigurationBuilder.AutoEnvAttributes.Disabled)
                .ApplicationInfo(
                    Components.ApplicationInfo().ApplicationId("mockId").ApplicationName("mockName")
                ).Build();

            var output = LdClientContext.MakeEnvironmentReporter(configuration);
            Assert.Equal("mockId", output.ApplicationInfo?.ApplicationId);
        }

        [Fact]
        public void TestMakeEnvironmentReporterDefaultsToSdkLayerWhenNothingSet()
        {
            var configuration = Configuration.Builder("aKey", ConfigurationBuilder.AutoEnvAttributes.Disabled)
                .Build();

            var output = LdClientContext.MakeEnvironmentReporter(configuration);
            Assert.Equal(SdkAttributes.Layer.ApplicationInfo?.ApplicationId, output.ApplicationInfo?.ApplicationId);
        }

        [Fact]
        public void TestMakeEnvironmentReporterUsesPlatformLayerWhenAutoEnvEnabled()
        {
            var configuration = Configuration.Builder("aKey", ConfigurationBuilder.AutoEnvAttributes.Enabled)
                .Build();

            var output = LdClientContext.MakeEnvironmentReporter(configuration);
            Assert.NotEqual(SdkAttributes.Layer.ApplicationInfo?.ApplicationId, output.ApplicationInfo?.ApplicationId);
        }

        [Fact]
        public void TestMakeEnvironmentReporterUsesApplicationInfoWhenSetAndAutoEnvEnabled()
        {
            var configuration = Configuration.Builder("aKey", ConfigurationBuilder.AutoEnvAttributes.Enabled)
                .ApplicationInfo(
                    Components.ApplicationInfo().ApplicationId("mockId").ApplicationName("mockName")
                ).Build();

            var output = LdClientContext.MakeEnvironmentReporter(configuration);
            Assert.Equal("mockId", output.ApplicationInfo?.ApplicationId);
        }
    }
}
