using System.Collections.Generic;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    public class FactoriesTest
    {
        [Fact]
        public void ReplaceIsWellBehaved()
        {
            var underTest = new Factories<string>(true, new[] { "1", "2", "3" });

            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());

            underTest.Replace(new[] { "4", "5", "6" });

            Assert.Equal("4", underTest.Next());
            Assert.Equal("5", underTest.Next());
            Assert.Equal("6", underTest.Next());
            Assert.Equal("4", underTest.Next());
            Assert.Equal("5", underTest.Next());
        }

        [Fact]
        public void CyclesCorrectlyAfterReplacingNonEmptyList()
        {
            var underTest = new Factories<string>(true, new[] { "1", "2", "3" });

            // initial cycling
            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());

            // remove head while list is non-empty
            Assert.True(underTest.Remove("1"));
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());

            // remove tail
            Assert.True(underTest.Remove("3"));
            Assert.Equal("2", underTest.Next());
            Assert.Equal("2", underTest.Next());

            // remove last element
            Assert.True(underTest.Remove("2"));
            Assert.Null(underTest.Next());
            Assert.Null(underTest.Next());
            Assert.Null(underTest.Next());

            // replace after list has become empty
            underTest.Replace(new[] { "4", "5", "6" });

            Assert.Equal("4", underTest.Next());
            Assert.Equal("5", underTest.Next());
            Assert.Equal("6", underTest.Next());

            // remove head of non-empty list
            Assert.True(underTest.Remove("4"));
            Assert.Equal("5", underTest.Next());
            Assert.Equal("6", underTest.Next());
            Assert.Equal("5", underTest.Next());
            Assert.Equal("6", underTest.Next());

            // remove tail of non-empty list
            Assert.True(underTest.Remove("6"));
            Assert.Equal("5", underTest.Next());
            Assert.Equal("5", underTest.Next());

            // remove last element
            Assert.True(underTest.Remove("5"));
            Assert.Null(underTest.Next());
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void CyclesCorrectlyAfterReplacingEmptyList()
        {
            var underTest = new Factories<string>(true, new List<string>());

            underTest.Replace(new[] { "1", "2", "3" });

            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());

            Assert.True(underTest.Remove("1"));
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());

            Assert.True(underTest.Remove("3"));
            Assert.Equal("2", underTest.Next());
            Assert.Equal("2", underTest.Next());

            Assert.True(underTest.Remove("2"));
            Assert.Null(underTest.Next());
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void RemovingHeadIsWellBehavedAtStart()
        {
            var underTest = new Factories<string>(true, new[] { "1", "2", "3" });

            // head is currently pointing at "1"
            Assert.True(underTest.Remove("1"));

            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());
            Assert.Equal("2", underTest.Next());
        }

        [Fact]
        public void RemovingHeadIsWellBehavedInMiddle()
        {
            var underTest = new Factories<string>(true, new[] { "1", "2", "3" });

            Assert.Equal("1", underTest.Next()); // head now pointing to "2"

            Assert.True(underTest.Remove("2"));

            Assert.Equal("3", underTest.Next());
            Assert.Equal("1", underTest.Next());
            Assert.Equal("3", underTest.Next());
        }

        [Fact]
        public void RemovingHeadIsWellBehavedAtEnd()
        {
            var underTest = new Factories<string>(true, new[] { "1", "2", "3" });

            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next()); // head now pointing to "3"

            Assert.True(underTest.Remove("3"));

            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());
            Assert.Equal("1", underTest.Next());
        }

        [Fact]
        public void RemovingExistingReturnsTrue()
        {
            var underTest = new Factories<string>(true, new[] { "1" });

            Assert.True(underTest.Remove("1"));
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void RemovingNonexistentReturnsFalse()
        {
            var underTest = new Factories<string>(true, new List<string>());

            Assert.False(underTest.Remove("1"));
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void SingleElementRemovedAndNextCalled()
        {
            var underTest = new Factories<string>(true, new[] { "1" });

            Assert.True(underTest.Remove("1"));
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void NonCircularListReturnsNullAfterConsumingAllElements()
        {
            var underTest = new Factories<string>(false, new[] { "1", "2", "3" });

            // Consume all elements
            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());

            // After consuming all elements, Next() should return null
            Assert.Null(underTest.Next());
            Assert.Null(underTest.Next());
        }
    }
}


