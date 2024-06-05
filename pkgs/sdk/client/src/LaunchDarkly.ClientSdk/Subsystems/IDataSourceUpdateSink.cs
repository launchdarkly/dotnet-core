using LaunchDarkly.Sdk.Client.Interfaces;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client.Subsystems
{
    // Note: In .NET server-side SDK 6.x, Java SDK 5.x, and Go SDK 5.x, where this component was added, it
    // is called "DataSourceUpdates". This name was thought to be a bit confusing, since it receives updates
    // rather than providing them, so as of .NET client-side SDK 2.x we are calling it an "update sink".

    /// <summary>
    /// Interface that an implementation of <see cref="IDataSource"/> will use to push data into the SDK.
    /// </summary>
    /// <remarks>
    /// The data source interacts with this object, rather than manipulating the data store directly, so
    /// that the SDK can perform any other necessary operations that must happen when data is updated.
    /// </remarks>
    public interface IDataSourceUpdateSink
    {
        /// <summary>
        /// Completely overwrites the current contents of the data store with a set of items for each collection.
        /// </summary>
        /// <param name="context">the current evaluation context</param>
        /// <param name="data">the data set</param>
        /// <returns>true if the update succeeded, false if it failed</returns>
        void Init(Context context, FullDataSet data);

        /// <summary>
        /// Updates or inserts an item. For updates, the object will only be updated if the existing
        /// version is less than the new version.
        /// </summary>
        /// <param name="context">the current evaluation context</param>
        /// <param name="key">the feature flag key</param>
        /// <param name="data">the item data</param>
        void Upsert(Context context, string key, ItemDescriptor data);

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
        /// <para>
        /// Data source implementations normally should not need to set the state to
        /// <see cref="DataSourceState.Valid"/>, because that will happen automatically if they call
        /// <see cref="Init(Context, FullDataSet)"/>.
        /// </para>
        /// </remarks>
        /// <param name="newState">the data source state</param>
        /// <param name="newError">information about a new error, if any</param>
        /// <seealso cref="IDataSourceStatusProvider"/>
        void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError);
    }
}
