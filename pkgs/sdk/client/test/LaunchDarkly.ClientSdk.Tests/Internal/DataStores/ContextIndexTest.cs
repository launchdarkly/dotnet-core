using System;
using System.Collections.Immutable;
using Xunit;

using static LaunchDarkly.TestHelpers.JsonAssertions;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    public class ContextIndexTest
    {
        [Fact]
        public void EmptyConstructor()
        {
            var ui = new ContextIndex();
            Assert.NotNull(ui.Data);
            Assert.Empty(ui.Data);
        }

        [Fact]
        public void Serialize()
        {
            var ui = new ContextIndex()
                .UpdateTimestamp("user1", UnixMillisecondTime.OfMillis(1000))
                .UpdateTimestamp("user2", UnixMillisecondTime.OfMillis(2000));

            var json = ui.Serialize();
            var expected = @"[[""user1"",1000],[""user2"",2000]]";

            AssertJsonEqual(expected, json);
        }

        [Fact]
        public void Deserialize()
        {
            var json = @"[[""user1"",1000],[""user2"",2000]]";
            var ui = ContextIndex.Deserialize(json);

            Assert.NotNull(ui.Data);
            Assert.Collection(ui.Data,
                AssertEntry("user1", 1000),
                AssertEntry("user2", 2000));
        }

        [Fact]
        public void DeserializeMalformedJson()
        {
            Assert.ThrowsAny<FormatException>(() =>
                ContextIndex.Deserialize("}"));

            Assert.ThrowsAny<FormatException>(() =>
                ContextIndex.Deserialize("["));

            Assert.ThrowsAny<FormatException>(() =>
                ContextIndex.Deserialize("[[true,1000]]"));

            Assert.ThrowsAny<FormatException>(() =>
                ContextIndex.Deserialize(@"[[""user1"",false]]"));

            Assert.ThrowsAny<FormatException>(() =>
                ContextIndex.Deserialize("[3]"));
        }

        [Fact]
        public void UpdateTimestampForExistingUser()
        {
            var ui = new ContextIndex()
                .UpdateTimestamp("user1", UnixMillisecondTime.OfMillis(1000))
                .UpdateTimestamp("user2", UnixMillisecondTime.OfMillis(2000));

            ui = ui.UpdateTimestamp("user1", UnixMillisecondTime.OfMillis(2001));

            Assert.Collection(ui.Data,
                AssertEntry("user2", 2000),
                AssertEntry("user1", 2001));
        }

        [Fact]
        public void PruneRemovesLeastRecentUsers()
        {
            var ui = new ContextIndex()
                .UpdateTimestamp("user1", UnixMillisecondTime.OfMillis(1000))
                .UpdateTimestamp("user2", UnixMillisecondTime.OfMillis(2000))
                .UpdateTimestamp("user3", UnixMillisecondTime.OfMillis(1111)) // deliberately out of order
                .UpdateTimestamp("user4", UnixMillisecondTime.OfMillis(3000))
                .UpdateTimestamp("user5", UnixMillisecondTime.OfMillis(4000));

            var ui1 = ui.Prune(3, out var removed);
            Assert.Equal(ImmutableList.Create("user1", "user3"), removed);
            Assert.Collection(ui1.Data,
                AssertEntry("user2", 2000),
                AssertEntry("user4", 3000),
                AssertEntry("user5", 4000));
        }

        [Fact]
        public void PruneWhenLimitIsNotExceeded()
        {
            var ui = new ContextIndex()
                .UpdateTimestamp("user1", UnixMillisecondTime.OfMillis(1000))
                .UpdateTimestamp("user2", UnixMillisecondTime.OfMillis(2000));

            Assert.Same(ui, ui.Prune(3, out var removed1));
            Assert.Empty(removed1);

            Assert.Same(ui, ui.Prune(2, out var removed2));
            Assert.Empty(removed2);
        }

        private Action<ContextIndex.IndexEntry> AssertEntry(string id, int millis) =>
            e =>
            {
                Assert.Equal(id, e.ContextId);
                Assert.Equal(UnixMillisecondTime.OfMillis(millis), e.Timestamp);
            };
    }
}
