using Xunit;

namespace LaunchDarkly.Sdk.EnvReporting
{
    public class EnvironmentReporterBuilderTest
    {

        [Fact]
        public void BuildWithNoParamsGivesNullProperty()
        {
            var builder = new EnvironmentReporterBuilder();
            var reporter = builder.Build();
            var actualAppInfo = reporter.ApplicationInfo;
            Assert.Null(actualAppInfo);
        }

        [Fact]
        public void TestPriorityOfLayersConfigLayerApplicationIdExists()
        {
            var configLayer = new ConfigLayerBuilder()
                .SetAppInfo(new ApplicationInfo("configId", "configName", "configVersion", "configVersionName"))
                .Build();

            var platformLayer = new Layer(new ApplicationInfo("platformId", "platformName",
                "platformVersion", "platformVersionName"), null, null, null);

            var builder = new EnvironmentReporterBuilder();
            builder.SetConfigLayer(configLayer);
            builder.SetPlatformLayer(platformLayer);
            var reporter = builder.Build();

            var expectedAppInfo = new ApplicationInfo("configId", "configName", "configVersion", "configVersionName");
            var actualAppInfo = reporter.ApplicationInfo;
            Assert.Equal(expectedAppInfo, actualAppInfo);
        }

        [Fact]
        public void TestPriorityOfLayersConfigLayerApplicationIdNotExists()
        {
            var configLayer = new ConfigLayerBuilder()
                .SetAppInfo(new ApplicationInfo(null, "these", "dont", "matter"))
                .Build();

            var platformLayer = new Layer(new ApplicationInfo("platformId", "platformName",
                "platformVersion", "platformVersionName"), null, null, null);

            var builder = new EnvironmentReporterBuilder();
            builder.SetConfigLayer(configLayer);
            builder.SetPlatformLayer(platformLayer);
            var reporter = builder.Build();

            var expectedAppInfo = new ApplicationInfo("platformId", "platformName",
                "platformVersion", "platformVersionName");
            var actualAppInfo = reporter.ApplicationInfo;
            Assert.Equal(expectedAppInfo, actualAppInfo);
        }
    }
}
