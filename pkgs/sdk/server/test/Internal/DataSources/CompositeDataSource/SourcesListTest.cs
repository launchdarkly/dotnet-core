using System.Collections.Generic;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    public class SourcesListTest
    {
        [Fact]
        public void ReplaceIsWellBehaved()
        {
            var underTest = new SourcesList<string>(true, new[] { "1", "2", "3" });

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
            var underTest = new SourcesList<string>(true, new[] { "1", "2", "3" });

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
            var underTest = new SourcesList<string>(true, new List<string>());

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
            var underTest = new SourcesList<string>(true, new[] { "1", "2", "3" });

            // head is currently pointing at "1"
            Assert.True(underTest.Remove("1"));

            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());
            Assert.Equal("2", underTest.Next());
        }

        [Fact]
        public void RemovingHeadIsWellBehavedInMiddle()
        {
            var underTest = new SourcesList<string>(true, new[] { "1", "2", "3" });

            Assert.Equal("1", underTest.Next()); // head now pointing to "2"

            Assert.True(underTest.Remove("2"));

            Assert.Equal("3", underTest.Next());
            Assert.Equal("1", underTest.Next());
            Assert.Equal("3", underTest.Next());
        }

        [Fact]
        public void RemovingHeadIsWellBehavedAtEnd()
        {
            var underTest = new SourcesList<string>(true, new[] { "1", "2", "3" });

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
            var underTest = new SourcesList<string>(true, new[] { "1" });

            Assert.True(underTest.Remove("1"));
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void RemovingNonexistentReturnsFalse()
        {
            var underTest = new SourcesList<string>(true, new List<string>());

            Assert.False(underTest.Remove("1"));
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void SingleElementRemovedAndNextCalled()
        {
            var underTest = new SourcesList<string>(true, new[] { "1" });

            Assert.True(underTest.Remove("1"));
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void NonCircularListReturnsNullAfterConsumingAllElements()
        {
            var underTest = new SourcesList<string>(false, new[] { "1", "2", "3" });

            // Consume all elements
            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());

            // After consuming all elements, Next() should return null
            Assert.Null(underTest.Next());
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void RemoveAllNullPredicateRemovesNothing()
        {
            var underTest = new SourcesList<string>(false, new[] { "1", "2", "3" });
            Assert.Equal(0, underTest.RemoveAll(null));
            Assert.Equal(3, underTest.Length);
            Assert.Equal("1", underTest.Next());
        }

        [Fact]
        public void RemoveAllOnEmptyListIsNoOp()
        {
            var underTest = new SourcesList<string>(false);
            Assert.Equal(0, underTest.RemoveAll(_ => true));
            Assert.Equal(0, underTest.Length);
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void RemoveAllPredicateMatchesNoneLeavesListIntact()
        {
            var underTest = new SourcesList<string>(false, new[] { "1", "2", "3" });
            Assert.Equal("1", underTest.Next()); // head -> 1 (pos = 1 after Next)

            Assert.Equal(0, underTest.RemoveAll(s => s == "missing"));
            Assert.Equal(3, underTest.Length);

            // Head is unchanged: next yields the previous next ("2").
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());
        }

        [Fact]
        public void RemoveAllPredicateMatchesEverythingClearsListAndResetsHead()
        {
            var underTest = new SourcesList<string>(true, new[] { "1", "2", "3" });
            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());

            Assert.Equal(3, underTest.RemoveAll(_ => true));
            Assert.Equal(0, underTest.Length);
            Assert.Equal(0, underTest.Pos);
            Assert.Null(underTest.Next());
            Assert.Null(underTest.Next());
        }

        [Fact]
        public void RemoveAllPredicateMatchesOnlyEntriesBeforeHeadAdjustsPos()
        {
            // After consuming "1" and "2", head is at index 2 ("3"). Remove "1" and "2": list
            // becomes ["3"] and head must point at "3" (pos = 0) so the next Next() returns "3".
            var underTest = new SourcesList<string>(false, new[] { "1", "2", "3" });
            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());

            Assert.Equal(2, underTest.RemoveAll(s => s == "1" || s == "2"));
            Assert.Equal(1, underTest.Length);
            Assert.Equal(0, underTest.Pos);
            Assert.Equal("3", underTest.Next());
        }

        [Fact]
        public void RemoveAllPredicateMatchesOnlyEntriesAfterHeadLeavesPosUnchanged()
        {
            // Head at index 1 ("2"). Removing entries that all live at indices > pos must not
            // shift pos -- the next Next() should still return "2".
            var underTest = new SourcesList<string>(false, new[] { "1", "2", "3", "4" });
            Assert.Equal("1", underTest.Next()); // pos = 1

            Assert.Equal(2, underTest.RemoveAll(s => s == "3" || s == "4"));
            Assert.Equal(2, underTest.Length);
            Assert.Equal(1, underTest.Pos);
            Assert.Equal("2", underTest.Next());
        }

        [Fact]
        public void RemoveAllPredicateMatchesHeadButNotPosIndex()
        {
            // Head at index 1 ("2"). Removing "2" itself: pos was 1; removed index 1 is NOT
            // before pos, so pos stays at 1, but the list shrinks so pos lands on the element
            // that used to be at index 2 ("3"). Subsequent Next() returns "3".
            var underTest = new SourcesList<string>(false, new[] { "1", "2", "3" });
            Assert.Equal("1", underTest.Next()); // pos = 1, head -> "2"

            Assert.Equal(1, underTest.RemoveAll(s => s == "2"));
            Assert.Equal(2, underTest.Length);
            Assert.Equal("3", underTest.Next());
        }

        [Fact]
        public void RemoveAllCircularResetsPosWhenListShrinksBelowOldPos()
        {
            // Circular list, all positions consumed; pos has wrapped. After RemoveAll leaves a
            // single element, pos must be reset to 0 so Next() returns it.
            var underTest = new SourcesList<string>(true, new[] { "1", "2", "3", "4" });
            Assert.Equal("1", underTest.Next());
            Assert.Equal("2", underTest.Next());
            Assert.Equal("3", underTest.Next());
            Assert.Equal("4", underTest.Next()); // circular: pos wraps back to 0

            Assert.Equal(3, underTest.RemoveAll(s => s != "2"));
            Assert.Equal(1, underTest.Length);
            Assert.Equal("2", underTest.Next());
        }

        [Fact]
        public void RemoveAllReturnsCountOfMatchedEntries()
        {
            var underTest = new SourcesList<string>(false, new[] { "a", "b", "a", "c", "a" });
            Assert.Equal(3, underTest.RemoveAll(s => s == "a"));
            Assert.Equal(2, underTest.Length);
            Assert.Equal("b", underTest.Next());
            Assert.Equal("c", underTest.Next());
        }
    }
}


