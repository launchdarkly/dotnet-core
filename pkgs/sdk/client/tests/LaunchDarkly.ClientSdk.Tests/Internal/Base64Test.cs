using Xunit;

namespace LaunchDarkly.Sdk.Client.Internal
{
    public class Base64Test
    {
        [Fact]
        public void TestUrlSafeBase64Encode()
        {
            Assert.Equal("eyJrZXkiOiJmb28-YmFyX18_In0=",
                Base64.UrlSafeEncode(@"{""key"":""foo>bar__?""}"));
        }

        [Fact]
        public void TestUrlSafeSha256Hash()
        {
            var input = "OhYeah?HashThis!!!"; // hash is KzDwVRpvTuf//jfMK27M4OMpIRTecNcJoaffvAEi+as= and it has a + and a /
            var expectedOutput = "KzDwVRpvTuf__jfMK27M4OMpIRTecNcJoaffvAEi-as=";
            Assert.Equal(expectedOutput, Base64.UrlSafeSha256Hash(input));
        }
    }
}
