using System;
using System.Collections.Immutable;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    public class ChangesetTest
    {
        [Fact]
        public void Change_ConstructorValidatesRequiredFields()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new Change(ChangeType.Put, null, "key", 1));

            Assert.Throws<ArgumentNullException>(() =>
                new Change(ChangeType.Put, "flag", null, 1));
        }

        [Fact]
        public void Change_CanCreatePutChange()
        {
            var obj = JsonDocument.Parse(@"{""key"":""test""}").RootElement;
            var change = new Change(ChangeType.Put, "flag", "test-flag", 5, obj);

            Assert.Equal(ChangeType.Put, change.Type);
            Assert.Equal("flag", change.Kind);
            Assert.Equal("test-flag", change.Key);
            Assert.Equal(5, change.Version);
            Assert.True(change.Object.HasValue);
        }

        [Fact]
        public void Change_CanCreateDeleteChange()
        {
            var change = new Change(ChangeType.Delete, "segment", "test-segment", 10);

            Assert.Equal(ChangeType.Delete, change.Type);
            Assert.Equal("segment", change.Kind);
            Assert.Equal("test-segment", change.Key);
            Assert.Equal(10, change.Version);
            Assert.False(change.Object.HasValue);
        }

        [Fact]
        public void ChangeSet_ConstructorValidatesChanges()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ChangeSet("xfer-full", null, "v1"));
        }

        [Fact]
        public void ChangeSet_CanCreateWithChanges()
        {
            var changes = ImmutableList.Create(
                new Change(ChangeType.Put, "flag", "flag1", 1),
                new Change(ChangeType.Delete, "segment", "segment1", 2)
            );

            var changeSet = new ChangeSet("xfer-changes", changes, "selector-123");

            Assert.Equal("xfer-changes", changeSet.IntentCode);
            Assert.Equal(2, changeSet.Changes.Count);
            Assert.Equal("selector-123", changeSet.Selector);
        }

        [Fact]
        public void ChangeSetBuilder_NoChanges_CreatesEmptyChangesetWithNoneIntent()
        {
            var changeSet = ChangeSetBuilder.NoChanges("v100");

            Assert.Equal("none", changeSet.IntentCode);
            Assert.Empty(changeSet.Changes);
            Assert.Equal("v100", changeSet.Selector);
        }

        [Fact]
        public void ChangeSetBuilder_Empty_CreatesEmptyChangesetWithFullIntent()
        {
            var changeSet = ChangeSetBuilder.Empty("v200");

            Assert.Equal("xfer-full", changeSet.IntentCode);
            Assert.Empty(changeSet.Changes);
            Assert.Equal("v200", changeSet.Selector);
        }

        [Fact]
        public void ChangeSetBuilder_Start_SetsIntentCodeFromServerIntent()
        {
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-full", "initial-sync")
            ));

            var builder = new ChangeSetBuilder();
            builder.Start(serverIntent);

            // Add a change and finish to verify intent was set
            var putObj = CreateTestPutObject("flag", "test-flag", 1);
            builder.AddPut(putObj);

            var changeSet = builder.Finish("v1");
            Assert.Equal("xfer-changes", changeSet.IntentCode); // Auto-converts from xfer-full
        }

        [Fact]
        public void ChangeSetBuilder_Start_ThrowsWhenServerIntentIsNull()
        {
            var builder = new ChangeSetBuilder();
            Assert.Throws<ArgumentNullException>(() => builder.Start(null));
        }

        [Fact]
        public void ChangeSetBuilder_ExpectChanges_ConvertsNoneToChanges()
        {
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "none", "up-to-date")
            ));

            var builder = new ChangeSetBuilder();
            builder.Start(serverIntent);
            builder.ExpectChanges();

            var changeSet = builder.Finish("v1");
            Assert.Equal("xfer-changes", changeSet.IntentCode);
        }

        [Fact]
        public void ChangeSetBuilder_ExpectChanges_DoesNotChangeOtherIntents()
        {
            var serverIntent1 = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-full", "initial-sync")
            ));
            var serverIntent2 = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-2", 200, "xfer-changes", "incremental")
            ));

            var builder1 = new ChangeSetBuilder();
            builder1.Start(serverIntent1);
            builder1.ExpectChanges();
            var changeSet1 = builder1.Finish("v1");
            Assert.Equal("xfer-full", changeSet1.IntentCode); // Does not convert xfer-full

            var builder2 = new ChangeSetBuilder();
            builder2.Start(serverIntent2);
            builder2.ExpectChanges();
            var changeSet2 = builder2.Finish("v2");
            Assert.Equal("xfer-changes", changeSet2.IntentCode); // Does not change xfer-changes
        }

        [Fact]
        public void ChangeSetBuilder_ExpectChanges_ThrowsWhenStartNotCalled()
        {
            var builder = new ChangeSetBuilder();
            var exception = Assert.Throws<InvalidOperationException>(() => builder.ExpectChanges());
            Assert.Contains("Cannot expect changes without a server-intent", exception.Message);
        }

        [Fact]
        public void ChangeSetBuilder_Reset_ClearsPendingChanges()
        {
            var builder = new ChangeSetBuilder();
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-changes", "incremental")
            ));

            builder.Start(serverIntent);
            builder.AddPut(CreateTestPutObject("flag", "flag1", 1));
            builder.AddPut(CreateTestPutObject("flag", "flag2", 2));

            builder.Reset();

            var changeSet = builder.Finish("v1");
            Assert.Empty(changeSet.Changes);
            Assert.Equal("xfer-changes", changeSet.IntentCode); // Intent preserved
        }

        [Fact]
        public void ChangeSetBuilder_AddPut_AddsChangeToList()
        {
            var builder = new ChangeSetBuilder();
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-changes", "incremental")
            ));

            builder.Start(serverIntent);

            var putObj = CreateTestPutObject("flag", "test-flag", 5);
            builder.AddPut(putObj);

            var changeSet = builder.Finish("v1");

            Assert.Single(changeSet.Changes);
            var change = changeSet.Changes[0];
            Assert.Equal(ChangeType.Put, change.Type);
            Assert.Equal("flag", change.Kind);
            Assert.Equal("test-flag", change.Key);
            Assert.Equal(5, change.Version);
            Assert.True(change.Object.HasValue);
        }

        [Fact]
        public void ChangeSetBuilder_AddPut_ThrowsWhenPutObjectIsNull()
        {
            var builder = new ChangeSetBuilder();
            Assert.Throws<ArgumentNullException>(() => builder.AddPut(null));
        }

        [Fact]
        public void ChangeSetBuilder_AddDelete_AddsChangeToList()
        {
            var builder = new ChangeSetBuilder();
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-changes", "incremental")
            ));

            builder.Start(serverIntent);

            var deleteObj = new DeleteObject(10, "segment", "test-segment");
            builder.AddDelete(deleteObj);

            var changeSet = builder.Finish("v1");

            Assert.Single(changeSet.Changes);
            var change = changeSet.Changes[0];
            Assert.Equal(ChangeType.Delete, change.Type);
            Assert.Equal("segment", change.Kind);
            Assert.Equal("test-segment", change.Key);
            Assert.Equal(10, change.Version);
            Assert.False(change.Object.HasValue);
        }

        [Fact]
        public void ChangeSetBuilder_AddDelete_ThrowsWhenDeleteObjectIsNull()
        {
            var builder = new ChangeSetBuilder();
            Assert.Throws<ArgumentNullException>(() => builder.AddDelete(null));
        }

        [Fact]
        public void ChangeSetBuilder_Finish_AutoConvertsFullTransferToChangesWhenChangesExist()
        {
            var builder = new ChangeSetBuilder();
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-full", "initial-sync")
            ));

            builder.Start(serverIntent);
            builder.AddPut(CreateTestPutObject("flag", "flag1", 1));

            var changeSet = builder.Finish("v1");

            Assert.Equal("xfer-changes", changeSet.IntentCode);
            Assert.Single(changeSet.Changes);
        }

        [Fact]
        public void ChangeSetBuilder_Finish_KeepsFullIntentWhenNoChanges()
        {
            var builder = new ChangeSetBuilder();
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-full", "initial-sync")
            ));

            builder.Start(serverIntent);
            // Don't add any changes

            var changeSet = builder.Finish("v1");

            Assert.Equal("xfer-full", changeSet.IntentCode);
            Assert.Empty(changeSet.Changes);
        }

        [Fact]
        public void ChangeSetBuilder_CanAddMultipleChanges()
        {
            var builder = new ChangeSetBuilder();
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-changes", "incremental")
            ));

            builder.Start(serverIntent);
            builder.AddPut(CreateTestPutObject("flag", "flag1", 1));
            builder.AddPut(CreateTestPutObject("flag", "flag2", 2));
            builder.AddDelete(new DeleteObject(3, "segment", "segment1"));
            builder.AddPut(CreateTestPutObject("segment", "segment2", 4));

            var changeSet = builder.Finish("v100");

            Assert.Equal(4, changeSet.Changes.Count);
            Assert.Equal("v100", changeSet.Selector);

            // Verify order is preserved
            Assert.Equal(ChangeType.Put, changeSet.Changes[0].Type);
            Assert.Equal("flag1", changeSet.Changes[0].Key);

            Assert.Equal(ChangeType.Put, changeSet.Changes[1].Type);
            Assert.Equal("flag2", changeSet.Changes[1].Key);

            Assert.Equal(ChangeType.Delete, changeSet.Changes[2].Type);
            Assert.Equal("segment1", changeSet.Changes[2].Key);

            Assert.Equal(ChangeType.Put, changeSet.Changes[3].Type);
            Assert.Equal("segment2", changeSet.Changes[3].Key);
        }

        [Fact]
        public void ChangeSetBuilder_CanReuseBuilderAfterFinish()
        {
            var builder = new ChangeSetBuilder();
            var serverIntent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-changes", "incremental")
            ));

            // First changeset
            builder.Start(serverIntent);
            builder.AddPut(CreateTestPutObject("flag", "flag1", 1));
            var changeSet1 = builder.Finish("v1");

            Assert.Single(changeSet1.Changes);

            // Second changeset - builder should start fresh
            builder.Start(serverIntent);
            builder.AddPut(CreateTestPutObject("flag", "flag2", 2));
            var changeSet2 = builder.Finish("v2");

            Assert.Single(changeSet2.Changes);
            Assert.Equal("flag2", changeSet2.Changes[0].Key);
        }

        [Fact]
        public void ChangeSetBuilder_FinishWithoutStart_ThrowsInvalidOperationException()
        {
            // Calling Finish() without Start() should throw an exception
            // This matches the Go behavior where Start() is required
            var builder = new ChangeSetBuilder();
            var exception = Assert.Throws<InvalidOperationException>(() => builder.Finish("v1"));
            Assert.Contains("Cannot complete changeset without a server-intent", exception.Message);
        }

        [Fact]
        public void ChangeSetBuilder_MultipleStartCalls_LastStartWins()
        {
            var builder = new ChangeSetBuilder();

            var serverIntent1 = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 100, "xfer-full", "initial")
            ));
            var serverIntent2 = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-2", 200, "xfer-changes", "incremental")
            ));

            builder.Start(serverIntent1);
            builder.AddPut(CreateTestPutObject("flag", "flag1", 1));

            // Start again - should clear changes and update intent
            builder.Start(serverIntent2);
            builder.AddPut(CreateTestPutObject("flag", "flag2", 2));

            var changeSet = builder.Finish("v1");

            Assert.Single(changeSet.Changes);
            Assert.Equal("flag2", changeSet.Changes[0].Key);
            Assert.Equal("xfer-changes", changeSet.IntentCode);
        }

        [Fact]
        public void ChangeSetBuilder_NoChanges_UsesProvidedSelector()
        {
            var changeSet = ChangeSetBuilder.NoChanges("custom-selector-123");

            Assert.Equal("none", changeSet.IntentCode);
            Assert.Empty(changeSet.Changes);
            Assert.Equal("custom-selector-123", changeSet.Selector);
        }

        private static PutObject CreateTestPutObject(string kind, string key, int version)
        {
            var obj = JsonDocument.Parse($@"{{""key"":""{key}"",""version"":{version}}}").RootElement;
            return new PutObject(version, kind, key, obj);
        }
    }
}