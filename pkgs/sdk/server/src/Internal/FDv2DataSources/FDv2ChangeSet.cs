using System;
using System.Collections.Immutable;
using System.Text.Json;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// Represents the type of change operation.
    /// </summary>
    internal enum FDv2ChangeType
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

    internal enum FDv2ChangeSetType
    {
        /// <summary>
        /// Changeset represent a full payload to use as a basis.
        /// </summary>
        Full,

        /// <summary>
        /// Changeset represents a partial payload to be applied to a basis.
        /// </summary>
        Partial,

        /// <summary>
        /// A changeset which indicates that no changes should be made.
        /// </summary>
        None,
    }

    /// <summary>
    /// Represents a single change to a data object.
    /// </summary>
    internal sealed class FDv2Change
    {
        /// <summary>
        /// The type of change operation.
        /// </summary>
        public FDv2ChangeType Type { get; }

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
        public FDv2Change(FDv2ChangeType type, string kind, string key, int version, JsonElement? obj = null)
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
    internal sealed class FDv2ChangeSet
    {
        /// <summary>
        /// The intent code indicating how the server intends to transfer data.
        /// </summary>
        public FDv2ChangeSetType Type { get; }

        /// <summary>
        /// The list of changes in this changeset.
        /// <para>
        /// Null if there are no changes to apply.
        /// </para>
        /// </summary>
        public ImmutableList<FDv2Change> Changes { get; }

        /// <summary>
        /// The selector (version identifier) for this changeset.
        /// </summary>
        public FDv2Selector FDv2Selector { get; }

        /// <summary>
        /// Constructs a new ChangeSet.
        /// </summary>
        /// <param name="type">The type of the changeset.</param>
        /// <param name="changes">The list of changes.</param>
        /// <param name="fDv2Selector">The selector.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="changes"/> is null.</exception>
        public FDv2ChangeSet(FDv2ChangeSetType type, ImmutableList<FDv2Change> changes, FDv2Selector fDv2Selector)
        {
            Type = type;
            Changes = changes ?? throw new ArgumentNullException(nameof(changes));
            FDv2Selector = fDv2Selector;
        }

        /// <summary>
        /// An empty changeset that indicates no changes are required.
        /// </summary>
        public static FDv2ChangeSet None { get; } =
            new FDv2ChangeSet(FDv2ChangeSetType.None, ImmutableList<FDv2Change>.Empty, FDv2Selector.Empty);
    }
}
