using Xunit;

namespace LaunchDarkly.Sdk
{
    public class ApplicationInfoBuilderTest
    {
        [Fact]
        public void IgnoresInvalidValues() {
            ApplicationInfoBuilder b = new ApplicationInfoBuilder();
            b.ApplicationId("im#invalid");
            b.ApplicationName("im#invalid");
            b.ApplicationVersion("im#invalid");
            b.ApplicationVersionName("im#invalid");
            ApplicationInfo info = b.Build();
            Assert.Null(info.ApplicationId);
            Assert.Null(info.ApplicationName);
            Assert.Null(info.ApplicationVersion);
            Assert.Null(info.ApplicationVersionName);
        }

        [Fact]
        public void SanitizesValues() {
            ApplicationInfoBuilder b = new ApplicationInfoBuilder();
            b.ApplicationId("id has spaces");
            b.ApplicationName("name has spaces");
            b.ApplicationVersion("version has spaces");
            b.ApplicationVersionName("version name has spaces");
            ApplicationInfo info = b.Build();
            Assert.Equal("id-has-spaces", info.ApplicationId);
            Assert.Equal("name-has-spaces", info.ApplicationName);
            Assert.Equal("version-has-spaces", info.ApplicationVersion);
            Assert.Equal("version-name-has-spaces", info.ApplicationVersionName);
        }

        [Fact]
        public void NullValueIsValid() {
            ApplicationInfoBuilder b = new ApplicationInfoBuilder();
            b.ApplicationId("myID"); // first non-null
            ApplicationInfo info = b.Build();
            Assert.Equal("myID", info.ApplicationId);

            b.ApplicationId(null); // now back to null
            ApplicationInfo info2 = b.Build();
            Assert.Null(info2.ApplicationId);
        }
    }
}
