using System;
using Xunit;

namespace LaunchDarkly.Sdk
{
    public class TestUtil
    {
        public static void AssertJsonEquals(string expected, string actual)
        {
            Assert.Equal(LdValue.Parse(expected), LdValue.Parse(actual));
        }
    }
}
