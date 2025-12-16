using LaunchDarkly.Sdk.Server.Interfaces;

namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Interfaces required by data source updates implementations in FDv2.
    /// </summary>
    internal interface IDataSourceUpdatesV2: ITransactionalDataSourceUpdates
    {
        /// <summary>
        /// An object that provides status tracking for the data store, if applicable.
        /// </summary>
        /// <remarks>
        /// This may be useful if the data source needs to be aware of storage problems that might require it
        /// to take some special action: for instance, if a database outage may have caused some data to be
        /// lost and therefore the data should be re-requested from LaunchDarkly.
        /// </remarks>
        IDataStoreStatusProvider DataStoreStatusProvider { get; }

        /// <summary>
        /// Informs the SDK of a change in the data source's status.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Data source implementations should use this method if they have any concept of being in a valid
        /// state, a temporarily disconnected state, or a permanently stopped state.
        /// </para>
        /// <para>
        /// If <paramref name="newState"/> is different from the previous state, and/or <paramref name="newError"/>
        /// is non-null, the SDK will start returning the new status(adding a timestamp for the change) from
        /// <see cref="IDataSourceStatusProvider.Status"/>, and will trigger status change events to any
        /// registered listeners.
        /// </para>
        /// <para>
        /// A special case is that if <paramref name="newState"/> is <see cref="DataSourceState.Interrupted"/>,
        /// but the previous state was <see cref="DataSourceState.Initializing"/>, the state will
        /// remain at <see cref="DataSourceState.Initializing"/> because
        /// <see cref="DataSourceState.Interrupted"/> is only meaningful after a successful startup.
        /// </para>
        /// </remarks>
        /// <param name="newState">the data source state</param>
        /// <param name="newError">information about a new error, if any</param>
        /// <seealso cref="IDataSourceStatusProvider"/>
        void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError);
    }
}
