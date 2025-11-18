using System;
using System.Collections.Immutable;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;
using Xunit;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    public class FDv2ProtocolHandlerTest
    {
        private static FDv2Event CreateServerIntentEvent(IntentCode intentCode, string payloadId = "test-payload",
            int target = 1, string reason = "test-reason")
        {
            var intent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload(payloadId, target, intentCode, reason)
            ));
            var json = JsonSerializer.Serialize(intent, GetJsonOptions());
            var data = JsonDocument.Parse(json).RootElement;
            return new FDv2Event(FDv2EventTypes.ServerIntent, data);
        }

        private static FDv2Event CreatePutObjectEvent(string kind, string key, int version, JsonElement? obj = null)
        {
            var putObj = new PutObject(version, kind, key, obj ?? JsonDocument.Parse("{}").RootElement);
            var json = JsonSerializer.Serialize(putObj, GetJsonOptions());
            var data = JsonDocument.Parse(json).RootElement;
            return new FDv2Event(FDv2EventTypes.PutObject, data);
        }

        private static FDv2Event CreateDeleteObjectEvent(string kind, string key, int version)
        {
            var deleteObj = new DeleteObject(version, kind, key);
            var json = JsonSerializer.Serialize(deleteObj, GetJsonOptions());
            var data = JsonDocument.Parse(json).RootElement;
            return new FDv2Event(FDv2EventTypes.DeleteObject, data);
        }

        private static FDv2Event CreatePayloadTransferredEvent(string state, int version)
        {
            var transferred = new PayloadTransferred(state, version);
            var json = JsonSerializer.Serialize(transferred, GetJsonOptions());
            var data = JsonDocument.Parse(json).RootElement;
            return new FDv2Event(FDv2EventTypes.PayloadTransferred, data);
        }

        private static FDv2Event CreateErrorEvent(string id, string reason)
        {
            var error = new Error(id, reason);
            var json = JsonSerializer.Serialize(error, GetJsonOptions());
            var data = JsonDocument.Parse(json).RootElement;
            return new FDv2Event(FDv2EventTypes.Error, data);
        }

        private static FDv2Event CreateGoodbyeEvent(string reason)
        {
            var goodbye = new Goodbye(reason);
            var json = JsonSerializer.Serialize(goodbye, GetJsonOptions());
            var data = JsonDocument.Parse(json).RootElement;
            return new FDv2Event(FDv2EventTypes.Goodbye, data);
        }

        private static FDv2Event CreateHeartbeatEvent()
        {
            return new FDv2Event(FDv2EventTypes.HeartBeat, JsonDocument.Parse("{}").RootElement);
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(ServerIntentConverter.Instance);
            options.Converters.Add(PutObjectConverter.Instance);
            options.Converters.Add(DeleteObjectConverter.Instance);
            options.Converters.Add(PayloadTransferredConverter.Instance);
            options.Converters.Add(ErrorConverter.Instance);
            options.Converters.Add(GoodbyeConverter.Instance);
            return options;
        }

        #region Section 2.2.2: SDK has up to date saved payload

        /// <summary>
        /// Tests the scenario from section 2.2.2 where the SDK has an up-to-date payload.
        /// The server responds with intentCode: none indicating no changes are needed.
        /// </summary>
        [Fact]
        public void ServerIntent_WithIntentCodeNone_ReturnsChangesetImmediately()
        {
            // Section 2.2.2: SDK has up to date saved payload
            var handler = new FDv2ProtocolHandler();
            var evt = CreateServerIntentEvent(IntentCode.None, "payload-123", 52, "up-to-date");

            var action = handler.HandleEvent(evt);

            Assert.IsType<FDv2ActionChangeset>(action);
            var changesetAction = (FDv2ActionChangeset)action;
            Assert.Equal(ChangeSetType.None, changesetAction.Changeset.Type);
            Assert.Empty(changesetAction.Changeset.Changes);
        }

        #endregion

        #region Section 2.1.1 & 2.2.1: SDK has no saved payload (Full Transfer)

        /// <summary>
        /// Tests the scenario from sections 2.1.1 and 2.2.1 where the SDK has no saved payload.
        /// The server responds with intentCode: xfer-full and sends a complete payload.
        /// </summary>
        [Fact]
        public void FullTransfer_AccumulatesChangesAndEmitsOnPayloadTransferred()
        {
            // Section 2.1.1 & 2.2.1: SDK has no saved payload and continues to get changes
            var handler = new FDv2ProtocolHandler();

            // Server-intent with xfer-full
            var intentEvt = CreateServerIntentEvent(IntentCode.TransferFull, "payload-123", 52, "payload-missing");
            var intentAction = handler.HandleEvent(intentEvt);
            Assert.IsType<FDv2ActionNone>(intentAction);

            // Put some objects
            var put1 = CreatePutObjectEvent("flag", "flag-123", 12);
            var put1Action = handler.HandleEvent(put1);
            Assert.IsType<FDv2ActionNone>(put1Action);

            var put2 = CreatePutObjectEvent("flag", "flag-abc", 12);
            var put2Action = handler.HandleEvent(put2);
            Assert.IsType<FDv2ActionNone>(put2Action);

            // Payload-transferred finalizes the changeset
            var transferredEvt = CreatePayloadTransferredEvent("(p:payload-123:52)", 52);
            var transferredAction = handler.HandleEvent(transferredEvt);

            Assert.IsType<FDv2ActionChangeset>(transferredAction);
            var changesetAction = (FDv2ActionChangeset)transferredAction;
            Assert.Equal(ChangeSetType.Full, changesetAction.Changeset.Type);
            Assert.Equal(2, changesetAction.Changeset.Changes.Count);
            Assert.Equal("flag-123", changesetAction.Changeset.Changes[0].Key);
            Assert.Equal("flag-abc", changesetAction.Changeset.Changes[1].Key);
            Assert.Equal("(p:payload-123:52)", changesetAction.Changeset.Selector.State);
            Assert.Equal(52, changesetAction.Changeset.Selector.Version);
        }

        /// <summary>
        /// Tests that a full transfer properly replaces any partial state.
        /// Requirement 3.3.1: SDK must prepare to fully replace its local payload representation.
        /// </summary>
        [Fact]
        public void FullTransfer_ReplacesPartialState()
        {
            // Requirement 3.3.1: Prepare to fully replace local payload representation
            var handler = new FDv2ProtocolHandler();

            // Start with an intent to transfer changes
            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferChanges, "p1", 1, "stale"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "flag-1", 1));

            // Now receive xfer-full - should replace/reset
            var fullIntent = CreateServerIntentEvent(IntentCode.TransferFull, "p1", 2, "outdated");
            handler.HandleEvent(fullIntent);

            // Send new full payload
            handler.HandleEvent(CreatePutObjectEvent("flag", "flag-2", 2));
            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:2)", 2));

            var changesetAction = (FDv2ActionChangeset)action;
            Assert.Equal(ChangeSetType.Full, changesetAction.Changeset.Type);
            // Should only have flag-2, not flag-1
            Assert.Single(changesetAction.Changeset.Changes);
            Assert.Equal("flag-2", changesetAction.Changeset.Changes[0].Key);
        }

        #endregion

        #region Section 2.1.2 & 2.2.3: SDK has stale saved payload (Incremental Changes)

        /// <summary>
        /// Tests the scenario from sections 2.1.2 and 2.2.3 where the SDK has a stale payload.
        /// The server responds with intentCode: xfer-changes and sends incremental updates.
        /// </summary>
        [Fact]
        public void IncrementalTransfer_AccumulatesChangesAndEmitsOnPayloadTransferred()
        {
            // Section 2.1.2 & 2.2.3: SDK has stale saved payload
            var handler = new FDv2ProtocolHandler();

            // Server-intent with xfer-changes
            var intentEvt = CreateServerIntentEvent(IntentCode.TransferChanges, "payload-123", 52, "stale");
            var intentAction = handler.HandleEvent(intentEvt);
            Assert.IsType<FDv2ActionNone>(intentAction);

            // Put and delete objects
            var put1 = CreatePutObjectEvent("flag", "flag-cat", 13);
            handler.HandleEvent(put1);

            var put2 = CreatePutObjectEvent("flag", "flag-dog", 13);
            handler.HandleEvent(put2);

            var delete1 = CreateDeleteObjectEvent("flag", "flag-bat", 13);
            handler.HandleEvent(delete1);

            var put3 = CreatePutObjectEvent("flag", "flag-cow", 14);
            handler.HandleEvent(put3);

            // Payload-transferred finalizes the changeset
            var transferredEvt = CreatePayloadTransferredEvent("(p:payload-123:52)", 52);
            var transferredAction = handler.HandleEvent(transferredEvt);

            Assert.IsType<FDv2ActionChangeset>(transferredAction);
            var changesetAction = (FDv2ActionChangeset)transferredAction;
            Assert.Equal(ChangeSetType.Partial, changesetAction.Changeset.Type);
            Assert.Equal(4, changesetAction.Changeset.Changes.Count);
            Assert.Equal(ChangeType.Put, changesetAction.Changeset.Changes[0].Type);
            Assert.Equal("flag-cat", changesetAction.Changeset.Changes[0].Key);
            Assert.Equal(ChangeType.Put, changesetAction.Changeset.Changes[1].Type);
            Assert.Equal("flag-dog", changesetAction.Changeset.Changes[1].Key);
            Assert.Equal(ChangeType.Delete, changesetAction.Changeset.Changes[2].Type);
            Assert.Equal("flag-bat", changesetAction.Changeset.Changes[2].Key);
            Assert.Equal(ChangeType.Put, changesetAction.Changeset.Changes[3].Type);
            Assert.Equal("flag-cow", changesetAction.Changeset.Changes[3].Key);
        }

        #endregion

        #region Requirement 3.3.2: Payload State Validity

        /// <summary>
        /// Requirement 3.3.2: SDK must not consider its local payload state X as valid until
        /// receiving the payload-transferred event for the corresponding payload state X.
        /// </summary>
        [Fact]
        public void PayloadTransferred_OnlyEmitsChangesetAfterReceivingEvent()
        {
            // Requirement 3.3.2: Only consider payload valid after payload-transferred
            var handler = new FDv2ProtocolHandler();

            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 1, "missing"));

            // Accumulate changes - should not emit changeset yet
            var action1 = handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));
            Assert.IsType<FDv2ActionNone>(action1);

            var action2 = handler.HandleEvent(CreatePutObjectEvent("flag", "f2", 1));
            Assert.IsType<FDv2ActionNone>(action2);

            // Only after payload-transferred should we get a changeset
            var action3 = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));
            Assert.IsType<FDv2ActionChangeset>(action3);
        }

        /// <summary>
        /// Tests that payload-transferred event throws if received without prior server-intent.
        /// </summary>
        [Fact]
        public void PayloadTransferred_WithoutServerIntent_ThrowsInvalidOperationException()
        {
            var handler = new FDv2ProtocolHandler();

            // Attempt to send payload-transferred without server-intent
            var transferredEvt = CreatePayloadTransferredEvent("(p:payload-123:52)", 52);

            Assert.Throws<InvalidOperationException>(() => handler.HandleEvent(transferredEvt));
        }

        #endregion

        #region Requirement 3.3.7 & 3.3.8: Error Handling

        /// <summary>
        /// Requirement 3.3.7: SDK must discard partially transferred data when an error event is encountered.
        /// Requirement 3.3.8: SDK should stay connected after receiving an application level error event.
        /// </summary>
        [Fact]
        public void Error_DiscardsPartiallyTransferredData()
        {
            // Requirements 3.3.7 & 3.3.8: Discard partial data on error, stay connected
            var handler = new FDv2ProtocolHandler();

            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 1, "missing"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f2", 1));

            // Error occurs - partial data should be discarded
            var errorEvt = CreateErrorEvent("p1", "Something went wrong");
            var errorAction = handler.HandleEvent(errorEvt);

            Assert.IsType<FDv2ActionError>(errorAction);
            var errorActionTyped = (FDv2ActionError)errorAction;
            Assert.Equal("p1", errorActionTyped.Id);
            Assert.Equal("Something went wrong", errorActionTyped.Reason);

            // Server recovers and resends
            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 1, "retry"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f3", 1));
            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));

            // Should only have f3, not f1 or f2
            var changesetAction = (FDv2ActionChangeset)action;
            Assert.Single(changesetAction.Changeset.Changes);
            Assert.Equal("f3", changesetAction.Changeset.Changes[0].Key);
        }

        /// <summary>
        /// Tests that error maintains the current state (Full vs. Changes) after clearing partial data.
        /// </summary>
        [Fact]
        public void Error_MaintainsCurrentState()
        {
            var handler = new FDv2ProtocolHandler();

            // Start with an intent to transfer changes
            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferChanges, "p1", 1, "stale"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));

            // Error occurs
            handler.HandleEvent(CreateErrorEvent("p1", "error"));

            // Continue receiving changes (no new server-intent)
            handler.HandleEvent(CreatePutObjectEvent("flag", "f2", 1));
            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));

            var changesetAction = (FDv2ActionChangeset)action;
            // Should still be Partial (the state is maintained).
            Assert.Equal(ChangeSetType.Partial, changesetAction.Changeset.Type);
            Assert.Single(changesetAction.Changeset.Changes);
            Assert.Equal("f2", changesetAction.Changeset.Changes[0].Key);
        }

        #endregion

        #region Requirement 3.3.5: Goodbye Handling

        /// <summary>
        /// Requirement 3.3.5: SDK must log a message at the info level when a goodbye event is encountered.
        /// The message must include the reason.
        /// </summary>
        [Fact]
        public void Goodbye_ReturnsGoodbyeActionWithReason()
        {
            // Requirement 3.3.5: Log goodbye with reason
            var handler = new FDv2ProtocolHandler();
            var goodbyeEvt = CreateGoodbyeEvent("Server is shutting down");

            var action = handler.HandleEvent(goodbyeEvt);

            Assert.IsType<FDv2ActionGoodbye>(action);
            var goodbyeAction = (FDv2ActionGoodbye)action;
            Assert.Equal("Server is shutting down", goodbyeAction.Reason);
        }

        #endregion

        #region Requirement 3.3.9: Heartbeat Handling

        /// <summary>
        /// Requirement 3.3.9: SDK must silently handle/ignore heartbeat events.
        /// </summary>
        [Fact]
        public void Heartbeat_IsSilentlyIgnored()
        {
            // Requirement 3.3.9: Silently ignore heartbeat events
            var handler = new FDv2ProtocolHandler();
            var heartbeatEvt = CreateHeartbeatEvent();

            var action = handler.HandleEvent(heartbeatEvt);

            Assert.IsType<FDv2ActionNone>(action);
        }

        #endregion

        #region Requirement 3.4.2: Multiple Payloads Handling

        /// <summary>
        /// Requirement 3.4.2: SDK must ignore all but the first payload of the server-intent event
        /// and must not crash/error when receiving messages that contain multiple payloads.
        /// </summary>
        [Fact]
        public void ServerIntent_WithMultiplePayloads_UsesOnlyFirstPayload()
        {
            // Requirement 3.4.2: Ignore all but the first payload
            var handler = new FDv2ProtocolHandler();

            var intent = new ServerIntent(ImmutableList.Create(
                new ServerIntentPayload("payload-1", 10, IntentCode.TransferChanges, "stale"),
                new ServerIntentPayload("payload-2", 20, IntentCode.None, "up-to-date")
            ));
            var json = JsonSerializer.Serialize(intent, GetJsonOptions());
            var data = JsonDocument.Parse(json).RootElement;
            var evt = new FDv2Event(FDv2EventTypes.ServerIntent, data);

            var action = handler.HandleEvent(evt);

            // Should return None because the first payload is TransferChanges (not None)
            Assert.IsType<FDv2ActionNone>(action);

            // Verify we're in Changes state by sending changes
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));
            var changesetAction =
                (FDv2ActionChangeset)handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));
            Assert.Equal(ChangeSetType.Partial, changesetAction.Changeset.Type);
        }

        #endregion

        #region Unknown Event Handling

        /// <summary>
        /// Tests that unknown event types are handled gracefully with an internal error action.
        /// </summary>
        [Fact]
        public void UnknownEventType_ReturnsInternalErrorAction()
        {
            var handler = new FDv2ProtocolHandler();
            var unknownEvt = new FDv2Event("unknown-event-type", JsonDocument.Parse("{}").RootElement);

            var action = handler.HandleEvent(unknownEvt);

            Assert.IsType<FDv2ActionInternalError>(action);
            var internalError = (FDv2ActionInternalError)action;
            Assert.Contains("unknown-event-type", internalError.Message);
        }

        #endregion

        #region State Transitions

        /// <summary>
        /// Tests that after payload-transferred, the handler transitions to Changes state
        /// to receive subsequent incremental updates.
        /// </summary>
        [Fact]
        public void PayloadTransferred_TransitionsToChangesState()
        {
            var handler = new FDv2ProtocolHandler();

            // Start with full transfer
            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 1, "missing"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));
            var action1 = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));

            var changeset1 = ((FDv2ActionChangeset)action1).Changeset;
            Assert.Equal(ChangeSetType.Full, changeset1.Type);

            // Now send more changes without new server-intent - should be Partial
            handler.HandleEvent(CreatePutObjectEvent("flag", "f2", 2));
            var action2 = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:2)", 2));

            var changeset2 = ((FDv2ActionChangeset)action2).Changeset;
            Assert.Equal(ChangeSetType.Partial, changeset2.Type);
        }

        /// <summary>
        /// Tests that IntentCode.None properly sets the state to Changes.
        /// </summary>
        [Fact]
        public void ServerIntent_WithIntentCodeNone_TransitionsToChangesState()
        {
            var handler = new FDv2ProtocolHandler();

            // Receive intent with None
            handler.HandleEvent(CreateServerIntentEvent(IntentCode.None, "p1", 1, "up-to-date"));

            // Now send incremental changes
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 2));
            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:2)", 2));

            var changeset = ((FDv2ActionChangeset)action).Changeset;
            Assert.Equal(ChangeSetType.Partial, changeset.Type);
        }

        #endregion

        #region Put and Delete Operations

        /// <summary>
        /// Tests that put-object events correctly accumulate with all required fields.
        /// Section 3.2: put-object contains payload objects that should be accepted with upsert semantics.
        /// </summary>
        [Fact]
        public void PutObject_AccumulatesWithAllFields()
        {
            // Section 3.2: put-object with upsert semantics
            var handler = new FDv2ProtocolHandler();

            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 1, "missing"));

            var flagData = JsonDocument.Parse(@"{""key"":""test-flag"",""on"":true}").RootElement;
            var putEvt = CreatePutObjectEvent("flag", "test-flag", 42, flagData);
            handler.HandleEvent(putEvt);

            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));

            var changeset = ((FDv2ActionChangeset)action).Changeset;
            Assert.Single(changeset.Changes);
            Assert.Equal(ChangeType.Put, changeset.Changes[0].Type);
            Assert.Equal("flag", changeset.Changes[0].Kind);
            Assert.Equal("test-flag", changeset.Changes[0].Key);
            Assert.Equal(42, changeset.Changes[0].Version);
            Assert.NotNull(changeset.Changes[0].Object);
        }

        /// <summary>
        /// Tests that delete-object events correctly accumulate.
        /// Section 3.3: delete-object contains payload objects that should be deleted.
        /// </summary>
        [Fact]
        public void DeleteObject_AccumulatesWithAllFields()
        {
            // Section 3.3: delete-object
            var handler = new FDv2ProtocolHandler();

            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferChanges, "p1", 1, "stale"));

            var deleteEvt = CreateDeleteObjectEvent("segment", "old-segment", 99);
            handler.HandleEvent(deleteEvt);

            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));

            var changeset = ((FDv2ActionChangeset)action).Changeset;
            Assert.Single(changeset.Changes);
            Assert.Equal(ChangeType.Delete, changeset.Changes[0].Type);
            Assert.Equal("segment", changeset.Changes[0].Kind);
            Assert.Equal("old-segment", changeset.Changes[0].Key);
            Assert.Equal(99, changeset.Changes[0].Version);
            Assert.Null(changeset.Changes[0].Object);
        }

        /// <summary>
        /// Tests that put and delete operations can be mixed in a single changeset.
        /// </summary>
        [Fact]
        public void PutAndDelete_CanBeMixedInSameChangeset()
        {
            var handler = new FDv2ProtocolHandler();

            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferChanges, "p1", 1, "stale"));

            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));
            handler.HandleEvent(CreateDeleteObjectEvent("flag", "f2", 1));
            handler.HandleEvent(CreatePutObjectEvent("segment", "s1", 1));
            handler.HandleEvent(CreateDeleteObjectEvent("segment", "s2", 1));

            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));

            var changeset = ((FDv2ActionChangeset)action).Changeset;
            Assert.Equal(4, changeset.Changes.Count);
            Assert.Equal(ChangeType.Put, changeset.Changes[0].Type);
            Assert.Equal("f1", changeset.Changes[0].Key);
            Assert.Equal(ChangeType.Delete, changeset.Changes[1].Type);
            Assert.Equal("f2", changeset.Changes[1].Key);
            Assert.Equal(ChangeType.Put, changeset.Changes[2].Type);
            Assert.Equal("s1", changeset.Changes[2].Key);
            Assert.Equal(ChangeType.Delete, changeset.Changes[3].Type);
            Assert.Equal("s2", changeset.Changes[3].Key);
        }

        #endregion

        #region Multiple Transfer Cycles

        /// <summary>
        /// Tests that the handler can process multiple complete transfer cycles.
        /// Simulates a streaming connection receiving multiple payload updates over time.
        /// </summary>
        [Fact]
        public void MultipleTransferCycles_AreHandledCorrectly()
        {
            // Section 2.1.1: "some time later" - multiple transfers
            var handler = new FDv2ProtocolHandler();

            // First full transfer
            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 52, "missing"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f2", 1));
            var action1 = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:52)", 52));

            var changeset1 = ((FDv2ActionChangeset)action1).Changeset;
            Assert.Equal(ChangeSetType.Full, changeset1.Type);
            Assert.Equal(2, changeset1.Changes.Count);

            // Second incremental transfer (some time later)
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 2));
            handler.HandleEvent(CreateDeleteObjectEvent("flag", "f2", 2));
            var action2 = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:53)", 53));

            var changeset2 = ((FDv2ActionChangeset)action2).Changeset;
            Assert.Equal(ChangeSetType.Partial, changeset2.Type);
            Assert.Equal(2, changeset2.Changes.Count);

            // Third incremental transfer
            handler.HandleEvent(CreatePutObjectEvent("flag", "f3", 3));
            var action3 = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:54)", 54));

            var changeset3 = ((FDv2ActionChangeset)action3).Changeset;
            Assert.Equal(ChangeSetType.Partial, changeset3.Type);
            Assert.Single(changeset3.Changes);
        }

        /// <summary>
        /// Tests that receiving a new server-intent during an ongoing transfer properly resets state.
        /// Per spec: "The SDK may receive multiple server-intent messages with xfer-full within one connection's lifespan."
        /// </summary>
        [Fact]
        public void NewServerIntent_DuringTransfer_ResetsState()
        {
            // Requirement 3.3.1: SDK may receive multiple server-intent messages
            var handler = new FDv2ProtocolHandler();

            // Start first transfer
            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 1, "missing"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));

            // Receive new server-intent before payload-transferred (e.g., server restarted)
            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 2, "reset"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f2", 2));
            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:2)", 2));

            var changeset = ((FDv2ActionChangeset)action).Changeset;
            // Should only have f2, the first transfer was abandoned
            Assert.Single(changeset.Changes);
            Assert.Equal("f2", changeset.Changes[0].Key);
        }

        #endregion

        #region Empty Payloads and Edge Cases

        /// <summary>
        /// Tests handling of a transfer with no objects.
        /// </summary>
        [Fact]
        public void Transfer_WithNoObjects_EmitsEmptyChangeset()
        {
            var handler = new FDv2ProtocolHandler();

            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "p1", 1, "missing"));
            // No put or delete events
            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:p1:1)", 1));

            var changeset = ((FDv2ActionChangeset)action).Changeset;
            Assert.Equal(ChangeSetType.Full, changeset.Type);
            Assert.Empty(changeset.Changes);
        }

        /// <summary>
        /// Tests that server-intent with an empty payload list returns internal error.
        /// </summary>
        [Fact]
        public void ServerIntent_WithEmptyPayloadList_ReturnsInternalError()
        {
            var handler = new FDv2ProtocolHandler();

            var intent = new ServerIntent(ImmutableList<ServerIntentPayload>.Empty);
            var json = JsonSerializer.Serialize(intent, GetJsonOptions());
            var data = JsonDocument.Parse(json).RootElement;
            var evt = new FDv2Event(FDv2EventTypes.ServerIntent, data);

            var action = handler.HandleEvent(evt);

            Assert.IsType<FDv2ActionInternalError>(action);
            var internalError = (FDv2ActionInternalError)action;
            Assert.Contains("No payload present", internalError.Message);
        }

        #endregion

        #region Selector Verification

        /// <summary>
        /// Tests that the selector is properly populated from payload-transferred event.
        /// </summary>
        [Fact]
        public void PayloadTransferred_PopulatesSelector()
        {
            var handler = new FDv2ProtocolHandler();

            handler.HandleEvent(CreateServerIntentEvent(IntentCode.TransferFull, "test-payload-id", 42, "missing"));
            handler.HandleEvent(CreatePutObjectEvent("flag", "f1", 1));
            var action = handler.HandleEvent(CreatePayloadTransferredEvent("(p:test-payload-id:42)", 42));

            var changeset = ((FDv2ActionChangeset)action).Changeset;
            Assert.False(changeset.Selector.IsEmpty);
            Assert.Equal("(p:test-payload-id:42)", changeset.Selector.State);
            Assert.Equal(42, changeset.Selector.Version);
        }

        /// <summary>
        /// Tests that ChangeSet.None has an empty selector.
        /// </summary>
        [Fact]
        public void ChangeSetNone_HasEmptySelector()
        {
            var handler = new FDv2ProtocolHandler();

            var action = handler.HandleEvent(CreateServerIntentEvent(IntentCode.None, "p1", 1, "up-to-date"));

            var changeset = ((FDv2ActionChangeset)action).Changeset;
            Assert.True(changeset.Selector.IsEmpty);
        }

        #endregion
    }
}
