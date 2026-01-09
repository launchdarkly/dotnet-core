namespace LaunchDarkly.Sdk.Server.Subsystems
{
    
    /// <summary>
    /// Interface that an implementation of <see cref="IDataSource"/> will use to push data into the SDK transactionally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The data source interacts with this object, rather than manipulating the data store directly, so
    /// that the SDK can perform any other necessary operations that must happen when data is updated. This
    /// object also provides a mechanism to report status changes.
    /// </para>
    /// <para>
    /// Component factories for <see cref="IDataSource"/> implementations will receive an implementation of this
    /// interface in the <see cref="LdClientContext.DataSourceUpdates"/> property of <see cref="LdClientContext"/>.
    /// </para>
    /// </remarks>
    /// <para>
    /// This interface is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is in early access. If you want access to this feature please join the EAP. https://launchdarkly.com/docs/sdk/features/data-saving-mode
    /// </para>
    public interface ITransactionalDataSourceUpdates
    {
        /// <summary>
        /// Apply the given change set to the store. This should be done atomically if possible.
        /// </summary>
        /// <param name="changeSet">the changeset to apply</param>
        bool Apply(DataStoreTypes.ChangeSet<DataStoreTypes.ItemDescriptor> changeSet);
    }
}
