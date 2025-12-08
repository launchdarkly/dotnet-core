using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Subsystems;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    public class FDv2ChangeSetTranslatorTest : BaseTest
    {
        public FDv2ChangeSetTranslatorTest(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        private static JsonElement CreateFlagJsonElement(string key, int version)
        {
            var json = $@"{{
                ""key"": ""{key}"",
                ""version"": {version},
                ""on"": true,
                ""fallthrough"": {{""variation"": 0}},
                ""variations"": [true, false]
            }}";
            return JsonDocument.Parse(json).RootElement;
        }

        private static JsonElement CreateSegmentJsonElement(string key, int version)
        {
            var json = $@"{{
                ""key"": ""{key}"",
                ""version"": {version}
            }}";
            return JsonDocument.Parse(json).RootElement;
        }

        [Fact]
        public void ToChangeSet_WithFullChangeset_ReturnsFullChangeSetType()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1, CreateFlagJsonElement("flag1", 1))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Equal(DataStoreTypes.ChangeSetType.Full, result.Type);
        }

        [Fact]
        public void ToChangeSet_WithPartialChangeset_ReturnsPartialChangeSetType()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1, CreateFlagJsonElement("flag1", 1))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Partial, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Equal(DataStoreTypes.ChangeSetType.Partial, result.Type);
        }

        [Fact]
        public void ToChangeSet_WithNoneChangeset_ReturnsNoneChangeSetType()
        {
            var changes = ImmutableList<FDv2Change>.Empty;
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.None, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Equal(DataStoreTypes.ChangeSetType.None, result.Type);
        }

        [Fact]
        public void ToChangeSet_WithUnknownChangeSetType_ThrowsArgumentOutOfRangeException()
        {
            var changes = ImmutableList<FDv2Change>.Empty;
            var fdv2ChangeSet = new FDv2ChangeSet((FDv2ChangeSetType)999, changes, Selector.Make(1, "state1"));

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger));

            Assert.Contains("Unknown FDv2ChangeSetType", exception.Message);
            Assert.Contains("implementation error", exception.Message);
        }

        [Fact]
        public void ToChangeSet_IncludesSelector()
        {
            var changes = ImmutableList<FDv2Change>.Empty;
            var selector = Selector.Make(42, "test-state");
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, selector);

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Equal(selector.Version, result.Selector.Version);
            Assert.Equal(selector.State, result.Selector.State);
        }

        [Fact]
        public void ToChangeSet_IncludesEnvironmentId()
        {
            var changes = ImmutableList<FDv2Change>.Empty;
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger, "test-env-id");

            Assert.Equal("test-env-id", result.EnvironmentId);
        }

        [Fact]
        public void ToChangeSet_WithNullEnvironmentId_ReturnsNullEnvironmentId()
        {
            var changes = ImmutableList<FDv2Change>.Empty;
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Null(result.EnvironmentId);
        }

        [Fact]
        public void ToChangeSet_WithPutOperation_DeserializesItem()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1, CreateFlagJsonElement("flag1", 1))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            var flagData = result.Data.First(kvp => kvp.Key.Name == "features");
            var item = flagData.Value.Items.First();
            Assert.Equal("flag1", item.Key);
            Assert.NotNull(item.Value.Item);
            Assert.Equal(1, item.Value.Version);
        }

        [Fact]
        public void ToChangeSet_WithDeleteOperation_CreatesDeletedDescriptor()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Delete, "flag", "flag1", 5)
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Partial, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            var flagData = result.Data.First(kvp => kvp.Key.Name == "features");
            var item = flagData.Value.Items.First();
            Assert.Equal("flag1", item.Key);
            Assert.Null(item.Value.Item);
            Assert.Equal(5, item.Value.Version);
        }

        [Fact]
        public void ToChangeSet_WithUnknownChangeType_ThrowsArgumentOutOfRangeException()
        {
            var changes = ImmutableList.Create(
                new FDv2Change((FDv2ChangeType)999, "flag", "flag1", 1)
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger));

            Assert.Contains("Unknown FDv2ChangeType", exception.Message);
            Assert.Contains("implementation error", exception.Message);
        }

        [Fact]
        public void ToChangeSet_WithMultipleFlags_GroupsByKind()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1, CreateFlagJsonElement("flag1", 1)),
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag2", 2, CreateFlagJsonElement("flag2", 2))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            var flagData = result.Data.First(kvp => kvp.Key.Name == "features");
            Assert.Equal(2, flagData.Value.Items.Count());
        }

        [Fact]
        public void ToChangeSet_WithFlagsAndSegments_CreatesMultipleDataKinds()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1, CreateFlagJsonElement("flag1", 1)),
                new FDv2Change(FDv2ChangeType.Put, "segment", "seg1", 1, CreateSegmentJsonElement("seg1", 1))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Equal(2, result.Data.Count());
            Assert.Contains(result.Data, kvp => kvp.Key.Name == "features");
            Assert.Contains(result.Data, kvp => kvp.Key.Name == "segments");
        }

        [Fact]
        public void ToChangeSet_WithUnknownKind_SkipsItemAndLogsWarning()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "unknown-kind", "item1", 1, CreateFlagJsonElement("item1", 1)),
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1, CreateFlagJsonElement("flag1", 1))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Single(result.Data);
            Assert.Contains(result.Data, kvp => kvp.Key.Name == "features");
            AssertLogMessage(true, Logging.LogLevel.Warn,
                "Unknown data kind 'unknown-kind' in changeset, skipping");
        }

        [Fact]
        public void ToChangeSet_WithPutOperationMissingObject_SkipsItemAndLogsWarning()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1),
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag2", 2, CreateFlagJsonElement("flag2", 2))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            var flagData = result.Data.First(kvp => kvp.Key.Name == "features");
            Assert.Single(flagData.Value.Items);
            Assert.Equal("flag2", flagData.Value.Items.First().Key);
            AssertLogMessage(true, Logging.LogLevel.Warn,
                "Put operation for flag/flag1 missing object data, skipping");
        }

        [Fact]
        public void ToChangeSet_WithEmptyChanges_ReturnsEmptyData()
        {
            var changes = ImmutableList<FDv2Change>.Empty;
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Empty(result.Data);
        }

        [Fact]
        public void ToChangeSet_WithMixedPutAndDelete_HandlesAllOperations()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1, CreateFlagJsonElement("flag1", 1)),
                new FDv2Change(FDv2ChangeType.Delete, "flag", "flag2", 2),
                new FDv2Change(FDv2ChangeType.Put, "segment", "seg1", 1, CreateSegmentJsonElement("seg1", 1))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Partial, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            Assert.Equal(2, result.Data.Count());

            var flagData = result.Data.First(kvp => kvp.Key.Name == "features");
            Assert.Equal(2, flagData.Value.Items.Count());

            var flag1 = flagData.Value.Items.First(item => item.Key == "flag1");
            Assert.NotNull(flag1.Value.Item);
            Assert.Equal(1, flag1.Value.Version);

            var flag2 = flagData.Value.Items.First(item => item.Key == "flag2");
            Assert.Null(flag2.Value.Item);
            Assert.Equal(2, flag2.Value.Version);

            var segmentData = result.Data.First(kvp => kvp.Key.Name == "segments");
            Assert.Single(segmentData.Value.Items);
        }

        [Fact]
        public void ToChangeSet_PreservesOrderOfChangesWithinKind()
        {
            var changes = ImmutableList.Create(
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag3", 3, CreateFlagJsonElement("flag3", 3)),
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag1", 1, CreateFlagJsonElement("flag1", 1)),
                new FDv2Change(FDv2ChangeType.Put, "flag", "flag2", 2, CreateFlagJsonElement("flag2", 2))
            );
            var fdv2ChangeSet = new FDv2ChangeSet(FDv2ChangeSetType.Full, changes, Selector.Make(1, "state1"));

            var result = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, TestLogger);

            var flagData = result.Data.First(kvp => kvp.Key.Name == "features");
            var items = flagData.Value.Items.ToList();
            Assert.Equal("flag3", items[0].Key);
            Assert.Equal("flag1", items[1].Key);
            Assert.Equal("flag2", items[2].Key);
        }
    }
}
