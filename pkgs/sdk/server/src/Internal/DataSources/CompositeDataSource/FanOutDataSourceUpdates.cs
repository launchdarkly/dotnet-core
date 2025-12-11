using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// An <see cref="IDataSourceUpdates"/> implementation that fans out calls to
    /// multiple underlying <see cref="IDataSourceUpdates"/> instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each call made to this instance is forwarded to every wrapped instance. For methods
    /// that return a <see cref="bool"/>, the result is <c>true</c> only if all wrapped
    /// instances return <c>true</c>.
    /// </para>
    /// </remarks>
    internal sealed class FanOutDataSourceUpdates : IDataSourceUpdates, ITransactionalDataSourceUpdates
    {
        private readonly IReadOnlyList<IDataSourceUpdates> _targets;

        /// <summary>
        /// Creates a new <see cref="FanOutDataSourceUpdates"/> instance.
        /// </summary>
        /// <param name="targets">The collection of updates sinks to fan out to.</param>
        public FanOutDataSourceUpdates(IReadOnlyList<IDataSourceUpdates> targets)
        {
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        }

        /// <inheritdoc/>
        public IDataStoreStatusProvider DataStoreStatusProvider
        {
            get
            {
                if (_targets.Count == 0)
                {
                    throw new InvalidOperationException("FanOutDataSourceUpdates has no target IDataSourceUpdates instances.");
                }

                // Use the first target's status provider as the representative provider.
                return _targets[0].DataStoreStatusProvider;
            }
        }

        /// <inheritdoc/>
        public bool Init(FullDataSet<ItemDescriptor> allData)
        {
            var allSucceeded = true;

            foreach (var t in _targets)
            {
                if (!t.Init(allData))
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded;
        }

        /// <inheritdoc/>
        public bool Upsert(DataStoreTypes.DataKind kind, string key, DataStoreTypes.ItemDescriptor item)
        {
            var allSucceeded = true;

            foreach (var t in _targets)
            {
                if (!t.Upsert(kind, key, item))
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded;
        }

        /// <inheritdoc/>
        public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
        {
            foreach (var t in _targets)
            {
                t.UpdateStatus(newState, newError);
            }
        }

        public bool Apply(ChangeSet<ItemDescriptor> changeSet)
        {
            var allSucceeded = true;

            foreach (var t in _targets)
            {
                if (!((ITransactionalDataSourceUpdates)t).Apply(changeSet))
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded;
        }
    }
}


