using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    internal enum FDv2ProtocolActionType
    {
        /// <summary>
        /// Indicates that a changeset should be emitted.
        /// </summary>
        Changeset,

        /// <summary>
        /// Indicates that an error has been encountered, and it should be logged.
        /// </summary>
        Error,

        /// <summary>
        /// Indicates that the server has expressed an intent to disconnect and the SDK should log the reason.
        /// </summary>
        Goodbye,

        /// <summary>
        /// Indicates that no special action should be taken.
        /// </summary>
        None,

        /// <summary>
        /// Indicates that an internal error has occured and should be logged.  
        /// </summary>
        InternalError,
    }

    internal interface IFDv2ProtocolAction
    {
        FDv2ProtocolActionType Action { get; }
    }

    internal sealed class FDv2ActionChangeset : IFDv2ProtocolAction
    {
        public FDv2ProtocolActionType Action => FDv2ProtocolActionType.Changeset;
        public ChangeSet Changeset { get; }

        public FDv2ActionChangeset(ChangeSet changeset)
        {
            Changeset = changeset;
        }
    }

    internal sealed class FDv2ActionError : IFDv2ProtocolAction
    {
        public FDv2ProtocolActionType Action => FDv2ProtocolActionType.Error;

        public string Id { get; }
        public string Reason { get; }

        public FDv2ActionError(string id, string reason)
        {
            Id = id;
            Reason = reason;
        }
    }

    internal sealed class FDv2ActionGoodbye : IFDv2ProtocolAction
    {
        public FDv2ProtocolActionType Action => FDv2ProtocolActionType.Goodbye;

        public string Reason { get; }

        public FDv2ActionGoodbye(string reason)
        {
            Reason = reason;
        }
    }

    internal sealed class FDv2ActionInternalError : IFDv2ProtocolAction
    {
        public FDv2ProtocolActionType Action => FDv2ProtocolActionType.InternalError;
        public string Message { get; }

        public FDv2ActionInternalError(string message)
        {
            Message = message;
        }
    }

    internal sealed class FDv2ActionNone : IFDv2ProtocolAction
    {
        public FDv2ProtocolActionType Action => FDv2ProtocolActionType.None;

        private FDv2ActionNone()
        {
        }

        public static FDv2ActionNone Instance { get; } = new FDv2ActionNone();
    }

    /// <summary>
    /// Implements the FDv2 protocol state machine for handling payload communication events.
    /// See: FDV2PL-payload-communication specification.
    /// </summary>
    /// <remarks>
    /// This handler processes events from Flag Delivery v2 and maintains the protocol state
    /// as defined in section 4 (Event Handling State Diagram) of the specification.
    /// The SDK must process events in sequence and only consider payload state valid after
    /// receiving a payload-transferred event (Requirement 3.3.2).
    /// </remarks>
    internal sealed class FDv2ProtocolHandler
    {
        private enum FDv2ProtocolState
        {
            /// <summary>
            /// No server intent has been expressed.
            /// </summary>
            Inactive,

            /// <summary>
            /// Currently receiving incremental changes.
            /// </summary>
            Changes,

            /// <summary>
            /// Currently receiving a full transfer.
            /// </summary>
            Full,
        }

        private readonly List<Change> _changes = new List<Change>();
        private FDv2ProtocolState _state = FDv2ProtocolState.Inactive;

        /// <summary>
        /// Handle a server-intent message and update the protocol state.
        /// </summary>
        /// <param name="intent">A server intent message</param>
        /// <returns>
        /// Optionally returns a changeset if the intent indicates that no changes are required.
        /// </returns>
        /// <remarks>
        /// Implements Requirement 3.3.1: The SDK must prepare to fully replace its local payload
        /// representation when a server-intent with code xfer-full is received.
        ///
        /// Implements Requirement 3.4.2: The SDK must ignore all but the first payload of the
        /// server-intent event and must not crash/error when receiving messages that contain
        /// multiple payloads.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if there is an unexpected intent code. This would represent an implementation error where the
        /// intent code enumeration was extended, but this switch case was not updated.
        /// </exception>
        private IFDv2ProtocolAction ServerIntent(ServerIntent intent)
        {
            // Requirement 3.4.2: Ignore all but the first payload
            var payload = intent.Payloads?.FirstOrDefault();
            if (payload == null) return new FDv2ActionInternalError("No payload present in server-intent");

            switch (payload.IntentCode)
            {
                case IntentCode.None:
                    // Payload is up to date, no changes needed (see section 2.2.2).
                    // Produce a changeset that indicates there are no changes.
                    // Receiving this payload can transition the SDK to being initialized.
                    // We will start listening to changes.
                    _state = FDv2ProtocolState.Changes;
                    _changes.Clear();
                    return new FDv2ActionChangeset(ChangeSet.None);
                case IntentCode.TransferFull:
                    // Requirement 3.3.1: Prepare to fully replace local payload representation.
                    // The server will send all objects via put-object events.
                    _state = FDv2ProtocolState.Full;
                    break;
                case IntentCode.TransferChanges:
                    // The server will send incremental changes (deltas) to update the payload.
                    _state = FDv2ProtocolState.Changes;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Clear any partial data from previous incomplete transfers
            _changes.Clear();
            return FDv2ActionNone.Instance;
        }

        /// <summary>
        /// Handles a put-object event.
        /// </summary>
        /// <param name="put">The put payload to handle.</param>
        /// <remarks>
        /// Put-object events contain payload objects that should be accepted with upsert semantics.
        /// These changes are accumulated until a payload-transferred event is received.
        /// See section 3.2 of the specification.
        /// </remarks>
        private void PutObject(PutObject put)
        {
            _changes.Add(new Change(
                ChangeType.Put, put.Kind, put.Key, put.Version, put.Object));
        }

        /// <summary>
        /// Handles a delete-object event.
        /// </summary>
        /// <param name="delete">The delete payload to handle.</param>
        /// <remarks>
        /// Delete-object events contain payload objects that should be deleted.
        /// These changes are accumulated until a payload-transferred event is received.
        /// See section 3.3 of the specification.
        /// </remarks>
        private void DeleteObject(DeleteObject delete)
        {
            _changes.Add(new Change(
                ChangeType.Delete, delete.Kind, delete.Key, delete.Version));
        }

        /// <summary>
        /// Handle a payload transferred message.
        /// </summary>
        /// <param name="payload">The payload.</param>
        /// <returns>A changeset for the payload.</returns>
        /// <remarks>
        /// Implements Requirement 3.3.2: The SDK must not consider its local payload state X as valid
        /// until receiving the payload-transferred event for the corresponding payload state X.
        /// This method finalizes the accumulated changes and returns the complete changeset.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Indicates that the protocol handler was not in a valid state to generate changeset.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if there is an unexpected intent code or state. This would represent an implementation error
        /// where an enumeration was extended, but the handling was not updated.
        /// </exception>
        private IFDv2ProtocolAction PayloadTransferred(PayloadTransferred payload)
        {
            var changeset = new ChangeSet(ChangesetType(), _changes.ToImmutableList(),
                Selector.Make(payload.Version, payload.State));
            _state = FDv2ProtocolState.Changes;
            _changes.Clear();
            return new FDv2ActionChangeset(changeset);
        }

        /// <summary>
        /// Handle an error message. An error will reset the current changes, but maintain the current state.
        /// </summary>
        /// <param name="error">The error to handle.</param>
        /// <remarks>
        /// Implements Requirement 3.3.7: The SDK must discard partially transferred data when an
        /// error event is encountered. The error event renders partially transferred data invalid.
        /// The flag delivery service is expected to recover automatically.
        ///
        /// Implements Requirement 3.3.8: The SDK should stay connected after receiving an application
        /// level error event. The flag delivery service will automatically recover and resume sending
        /// the necessary updates.
        /// </remarks>
        private IFDv2ProtocolAction Error(Error error)
        {
            // Requirement 3.3.7: Discard partially transferred data
            _changes.Clear();
            return new FDv2ActionError(error.Id, error.Reason);
        }

        /// <summary>
        /// Handle a goodbye message.
        /// </summary>
        /// <param name="intent">The goodbye message.</param>
        /// <returns>Action indicating the server is about to disconnect.</returns>
        /// <remarks>
        /// Implements Requirement 3.3.5: The SDK must log a message at the info level when a goodbye
        /// event is encountered, including the reason provided by the server.
        /// </remarks>
        private IFDv2ProtocolAction Goodbye(Goodbye intent)
        {
            return new FDv2ActionGoodbye(intent.Reason);
        }

        /// <summary>
        /// Get the change set type based on the current state.
        /// </summary>
        /// <returns>The changeset type.</returns>
        /// <exception cref="InvalidOperationException">
        /// Indicates that the protocol handler was not in a valid state to generate changeset.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown if there is an unexpected intent code. This would represent an implementation error where the
        /// intent code enumeration was extended, but this switch case was not updated.
        /// </exception>
        private ChangeSetType ChangesetType()
        {
            switch (_state)
            {
                case FDv2ProtocolState.Inactive:
                    throw new InvalidOperationException(
                        "A payload transferred has been received without an intent having been established.");
                case FDv2ProtocolState.Changes:
                    return ChangeSetType.Partial;
                case FDv2ProtocolState.Full:
                    return ChangeSetType.Full;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Process an FDv2 event and update the protocol state accordingly.
        /// </summary>
        /// <param name="evt">The event to process.</param>
        /// <returns>
        /// An action indicating what the caller should do in response to this event.
        /// Actions may include emitting a changeset, logging an error, or taking no action.
        /// </returns>
        /// <remarks>
        /// This is the main entry point for processing FDv2 events. Events should be processed
        /// in the order they are received from Flag Delivery.
        ///
        /// Event handling behavior:
        /// - server-intent: Updates protocol state and may return a changeset if no changes are needed
        /// - put-object: Accumulates changes (returns None)
        /// - delete-object: Accumulates changes (returns None)
        /// - payload-transferred: Finalizes changes and returns a changeset (Requirement 3.3.2)
        /// - error: Discards partial data and returns error action (Requirements 3.3.7, 3.3.8)
        /// - goodbye: Returns goodbye action for logging (Requirement 3.3.5)
        /// - heartbeat: Silently ignored (Requirement 3.3.9)
        /// - unknown: Returns internal error action
        /// </remarks>
        public IFDv2ProtocolAction HandleEvent(FDv2Event evt)
        {
            switch (evt.EventType)
            {
                case FDv2EventTypes.ServerIntent:
                    return ServerIntent(evt.AsServerIntent());
                case FDv2EventTypes.DeleteObject:
                    DeleteObject(evt.AsDeleteObject());
                    break;
                case FDv2EventTypes.PutObject:
                    PutObject(evt.AsPutObject());
                    break;
                case FDv2EventTypes.Error:
                    return Error(evt.AsError());
                case FDv2EventTypes.Goodbye:
                    return Goodbye(evt.AsGoodbye());
                case FDv2EventTypes.PayloadTransferred:
                    return PayloadTransferred(evt.AsPayloadTransferred());
                case FDv2EventTypes.HeartBeat:
                    // Requirement 3.3.9: Silently handle/ignore heartbeat events
                    break;
                default:
                    return new FDv2ActionInternalError($"Received an unknown event of type {evt.EventType}");
            }

            return FDv2ActionNone.Instance;
        }
    }
}
