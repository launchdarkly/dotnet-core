namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Interface for a data store that holds feature flags and related data received by the SDK.
    /// This interface supports updating the store transactionally using ChangeSets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordinarily, the only implementation of this interface is the default in-memory
    /// implementation, which holds references to actual SDK data model objects. Any data store
    /// implementation that uses an external store, such as a database, should instead use
    /// <see cref="IPersistentDataStore"/> or <see cref="IPersistentDataStoreAsync"/>.
    /// </para>
    /// <para>
    /// Implementations must be thread-safe.
    /// </para>
    /// </remarks>
    /// <seealso cref="IPersistentDataStore"/>
    /// <seealso cref="IPersistentDataStoreAsync"/>
    /// <para>
    /// This interface is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is in early access. If you want access to this feature please join the EAP. https://launchdarkly.com/docs/sdk/features/data-saving-mode
    /// </para>
    public interface ITransactionalDataStore
    {
        /// <summary>
        /// Apply the given change set to the store. This should be done atomically if possible.
        /// </summary>
        /// <param name="changeSet">the changeset to apply</param>
        void Apply(DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> changeSet);
        
        /// <summary>
        /// The selector for the currently stored data. The selector will be non-null but may be empty.
        /// </summary>
        Selector Selector { get; }
    }
}
