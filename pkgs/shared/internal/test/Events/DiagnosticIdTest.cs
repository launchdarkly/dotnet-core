using System;
using Xunit;

namespace LaunchDarkly.Sdk.Internal.Events
{
    public class DiagnosticIdTest
    {

        [Fact]
        public void DiagnosticIdTakesKeySuffix()
        {
            DiagnosticId id = new DiagnosticId("suffix-of-sdkkey", Guid.NewGuid());
            Assert.Equal("sdkkey", id.SdkKeySuffix);
        }

        [Fact]
        public void DiagnosticIdTakesKeySuffixOfShortKey()
        {
            DiagnosticId id = new DiagnosticId("abc", Guid.NewGuid());
            Assert.Equal("abc", id.SdkKeySuffix);
        }

        [Fact]
        public void DiagnosticIdTakesKeySuffixOfEmptyKey()
        {
            DiagnosticId id = new DiagnosticId("", Guid.NewGuid());
            Assert.Equal("", id.SdkKeySuffix);
        }

        [Fact]
        public void DiagnosticIdDoesNotCrashWithNullKey()
        {
            DiagnosticId id = new DiagnosticId(null, Guid.NewGuid());
            Assert.Null(id.SdkKeySuffix);
        }
    }
}
