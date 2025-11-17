using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// Represents the type of change operation.
    /// </summary>
    internal enum ChangeType
    {
        /// <summary>
        /// Indicates an upsert operation (insert or update).
        /// </summary>
        Put,

        /// <summary>
        /// Indicates a delete operation.
        /// </summary>
        Delete
    }

    /// <summary>
    /// Represents a single change to a data object.
    /// </summary>
    internal sealed class Change
    {
        /// <summary>
        /// The type of change operation.
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public ChangeType Type { get; }

        /// <summary>
        /// The kind of object being changed ("flag" or "segment").
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// The key identifying the object.
        /// <para>
        /// This field is required and will never be null.
        /// </para>
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The version of the change.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// The serialized object data (only present for Put operations).
        /// </summary>
        public JsonElement? Object { get; }

        /// <summary>
        /// Constructs a new Change.
        /// </summary>
        /// <param name="type">The type of change operation.</param>
        /// <param name="kind">The kind of object being changed.</param>
        /// <param name="key">The key identifying the object.</param>
        /// <param name="version">The version of the change.</param>
        /// <param name="obj">The serialized object data (required for Put operations).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="kind"/> or <paramref name="key"/> is null.</exception>
        public Change(ChangeType type, string kind, string key, int version, JsonElement? obj = null)
        {
            Type = type;
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Version = version;
            Object = obj;
        }
    }

    /// <summary>
    /// Represents a collection of changes with metadata about the intent and version.
    /// </summary>
    internal sealed class ChangeSet
    {
        /// <summary>
        /// The intent code indicating how the server intends to transfer data.
        /// </summary>
        public string IntentCode { get; }

        /// <summary>
        /// The list of changes in this changeset.
        /// <para>
        /// This field will never be null.
        /// </para>
        /// </summary>
        public ImmutableList<Change> Changes { get; }

        /// <summary>
        /// The selector (version identifier) for this changeset.
        /// </summary>
        public string Selector { get; }

        /// <summary>
        /// Constructs a new ChangeSet.
        /// </summary>
        /// <param name="intentCode">The intent code.</param>
        /// <param name="changes">The list of changes.</param>
        /// <param name="selector">The selector (version identifier).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="changes"/> is null.</exception>
        public ChangeSet(string intentCode, ImmutableList<Change> changes, string selector)
        {
            IntentCode = intentCode;
            Changes = changes ?? throw new ArgumentNullException(nameof(changes));
            Selector = selector;
        }
    }

    /// <summary>
    /// Builder for constructing ChangeSet instances. Manages state transitions between
    /// full synchronization and incremental change modes.
    /// </summary>
    internal sealed class ChangeSetBuilder
    {
        private const string IntentTransferFull = "xfer-full";
        private const string IntentTransferChanges = "xfer-changes";
        private const string IntentNone = "none";

        private string _intentCode;
        private readonly List<Change> _changes;

        /// <summary>
        /// Constructs a new ChangeSetBuilder.
        /// </summary>
        public ChangeSetBuilder()
        {
            _changes = new List<Change>();
        }

        /// <summary>
        /// Creates a changeset indicating no changes are needed.
        /// </summary>
        /// <param name="selector">The version identifier.</param>
        /// <returns>A changeset with intent "none" and no changes.</returns>
        public static ChangeSet NoChanges(string selector)
        {
            return new ChangeSet(IntentNone, ImmutableList<Change>.Empty, selector);
        }

        /// <summary>
        /// Creates a changeset indicating all existing data should be cleared.
        /// </summary>
        /// <param name="selector">The version identifier.</param>
        /// <returns>A changeset with intent "xfer-full" and no changes, signaling a data reset.</returns>
        public static ChangeSet Empty(string selector)
        {
            return new ChangeSet(IntentTransferFull, ImmutableList<Change>.Empty, selector);
        }

        /// <summary>
        /// Initializes the builder with a server intent, setting the operation mode.
        /// </summary>
        /// <param name="serverIntent">The server intent containing the intent code.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="serverIntent"/> is null.</exception>
        public void Start(ServerIntent serverIntent)
        {
            if (serverIntent == null)
                throw new ArgumentNullException(nameof(serverIntent));

            // Use the intent code from the first payload if available
            if (serverIntent.Payloads.Count > 0)
            {
                _intentCode = serverIntent.Payloads[0].IntentCode;
            }

            _changes.Clear();
        }

        /// <summary>
        /// Ensures that the intent is set to "xfer-changes" when the current intent is "none".
        /// This transitions the builder from a "no changes needed" state to a "ready for future changes" state.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when Start() has not been called yet.</exception>
        public void ExpectChanges()
        {
            switch (_intentCode)
            {
                case null:
                    throw new InvalidOperationException("Cannot expect changes without a server-intent. Call Start() first.");
                case IntentNone:
                    _intentCode = IntentTransferChanges;
                    break;
            }
        }

        /// <summary>
        /// Clears all pending changes while preserving the intent code.
        /// </summary>
        public void Reset()
        {
            _changes.Clear();
        }

        /// <summary>
        /// Adds a put (upsert) operation to the changeset.
        /// </summary>
        /// <param name="putObject">The put object containing the data to upsert.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="putObject"/> is null.</exception>
        public void AddPut(PutObject putObject)
        {
            if (putObject == null)
                throw new ArgumentNullException(nameof(putObject));

            _changes.Add(new Change(
                ChangeType.Put,
                putObject.Kind,
                putObject.Key,
                putObject.Version,
                putObject.Object
            ));
        }

        /// <summary>
        /// Adds a delete operation to the changeset.
        /// </summary>
        /// <param name="deleteObject">The delete object specifying what to remove.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deleteObject"/> is null.</exception>
        public void AddDelete(DeleteObject deleteObject)
        {
            if (deleteObject == null)
                throw new ArgumentNullException(nameof(deleteObject));

            _changes.Add(new Change(
                ChangeType.Delete,
                deleteObject.Kind,
                deleteObject.Key,
                deleteObject.Version
            ));
        }

        /// <summary>
        /// Finalizes the changeset with the given selector. If the intent is "xfer-full" and there are
        /// changes, automatically converts to "xfer-changes" since this represents a successful full sync
        /// with data.
        /// </summary>
        /// <param name="selector">The version identifier for this changeset.</param>
        /// <returns>The completed changeset.</returns>
        /// <exception cref="InvalidOperationException">Thrown when Start() has not been called yet.</exception>
        public ChangeSet Finish(string selector)
        {
            if (_intentCode == null)
            {
                throw new InvalidOperationException("Cannot complete changeset without a server-intent. Call Start() first.");
            }

            var intentCode = _intentCode;

            // Auto-convert full transfer to incremental if we have changes
            // This represents a successful full sync transitioning to incremental mode
            if (intentCode == IntentTransferFull && _changes.Count > 0)
            {
                intentCode = IntentTransferChanges;
            }

            return new ChangeSet(intentCode, _changes.ToImmutableList(), selector);
        }
    }
}
