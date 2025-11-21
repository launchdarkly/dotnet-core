using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// An <see cref="IDataSourceUpdates"/> implementation that decorates an underlying
    /// <see cref="IDataSourceUpdates"/> and can map callbacks/events to actions on the
    /// <see cref="CompositeSource"/>.
    /// </summary>
    internal sealed class DataSourceUpdatesToActionMapper : IDataSourceUpdates
    {
        private readonly IDataSourceUpdates _inner;
        private readonly Action<CompositeSourceAction> _handleAction;

        /// <summary>
        /// Creates a new <see cref="DataSourceUpdatesToActionMapper"/>.
        /// </summary>
        /// <param name="inner">the underlying updates sink to delegate to</param>
        /// <param name="handleAction">
        /// optional callback used to dispatch <see cref="CompositeSourceAction"/> instances
        /// back to the owning <see cref="CompositeSource"/>.
        /// </param>
        public DataSourceUpdatesToActionMapper(
            IDataSourceUpdates inner,
            Action<CompositeSourceAction> handleAction = null
            )
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _handleAction = handleAction;
        }

        /// <summary>
        /// The underlying <see cref="IDataStoreStatusProvider"/> from the decorated sink.
        /// </summary>
        public IDataStoreStatusProvider DataStoreStatusProvider => _inner.DataStoreStatusProvider;

        public bool Init(FullDataSet<ItemDescriptor> allData)
        {
            // Currently this mapper is a simple pass-through; future behavior that maps
            // updates to CompositeSourceAction instances can be added here.
            return _inner.Init(allData);
        }

        public bool Upsert(DataKind kind, string key, ItemDescriptor item)
        {
            return _inner.Upsert(kind, key, item);
        }

        public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
        {
            _inner.UpdateStatus(newState, newError);
        }

        public bool InitWithHeaders(
            FullDataSet<ItemDescriptor> allData,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers
            )
        {
            if (_inner is IDataSourceUpdatesHeaders headersInner)
            {
                return headersInner.InitWithHeaders(allData, headers);
            }

            return _inner.Init(allData);
        }

        /// <summary>
        /// Helper for subclasses or future enhancements to dispatch an action, if a handler
        /// was provided.
        /// </summary>
        /// <param name="action">the action to dispatch</param>
        private void DispatchAction(CompositeSourceAction action)
        {
            _handleAction?.Invoke(action);
        }
    }
}


