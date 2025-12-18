using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// An <see cref="IDataSourceUpdates"/> implementation that forwards calls to
    /// multiple <see cref="IDataSourceObserver"/> instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each call made to this instance is forwarded to every wrapped instance. For methods
    /// that return a <see cref="bool"/>, the result is <c>true</c> only if all wrapped
    /// instances return <c>true</c>.
    /// </para>
    /// </remarks>
    internal sealed class ObservableDataSourceUpdates : IDataSourceUpdatesV2
    {
        private readonly IDataSourceUpdatesV2 _primary;
        private readonly IReadOnlyList<IDataSourceObserver> _secondaries;

        /// <summary>
        /// Creates a new <see cref="ObservableDataSourceUpdates"/> instance.
        /// </summary>
        /// <param name="primary">The primary updates sink to forward to.</param>
        /// <param name="secondaries">The collection of observers to forward to.</param>
        /// <param name="isLast">It is the last data source in the composite</param>
        public ObservableDataSourceUpdates(IDataSourceUpdatesV2 primary, IReadOnlyList<IDataSourceObserver> secondaries, bool isLast = false)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _secondaries = secondaries ?? throw new ArgumentNullException(nameof(secondaries));
        }

        /// <inheritdoc/>
        public IDataStoreStatusProvider DataStoreStatusProvider
        {
            get
            {
                return _primary.DataStoreStatusProvider;
            }
        }
        
        /// <inheritdoc/>
        public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
        {
            // First invoke on the primary
            _primary.UpdateStatus(newState, newError);

            // Then invoke on the secondaries
            foreach (var secondary in _secondaries)
            {
                secondary.UpdateStatus(newState, newError);
            }
        }

        public bool Apply(ChangeSet<ItemDescriptor> changeSet)
        {
            // First invoke on the primary
            var primarySucceeded = _primary.Apply(changeSet);

            // Then invoke on the secondaries
            foreach (var secondary in _secondaries)
            {
                secondary.Apply(changeSet);
            }

            return primarySucceeded;
        }
    }
}


