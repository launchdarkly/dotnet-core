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

        [Fact]
        public void TestUrlSafeEncodeNoPaddingStripsPadding()
        {
            // Same input as TestUrlSafeBase64Encode, whose encoding ends with a '=' pad.
            Assert.Equal("eyJrZXkiOiJmb28-YmFyX18_In0",
                Base64.UrlSafeEncodeNoPadding(@"{""key"":""foo>bar__?""}"));
        }

        [Fact]
        public void TestUrlSafeEncodeNoPaddingLeavesUnpaddedInputUnchanged()
        {
            // "ld_device" encodes to a length that needs no padding.
            Assert.Equal("bGRfZGV2aWNl", Base64.UrlSafeEncodeNoPadding("ld_device"));
        }

        [Fact]
        public void TestUrlSafeEncodeNoPaddingNeverContainsEquals()
        {
            foreach (var input in new[] { "ld_device", "ld_application", "org", "my.kind", "a", "ab" })
            {
                Assert.DoesNotContain("=", Base64.UrlSafeEncodeNoPadding(input));
            }
        }
    }
}
