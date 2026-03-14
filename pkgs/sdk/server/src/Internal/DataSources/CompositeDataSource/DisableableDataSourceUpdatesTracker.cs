using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.Sdk.Server.Interfaces;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Wraps provided instances of <see cref="IDataSourceUpdates"/> in a new instance that can be disabled when necessary.
    /// </summary>
    internal sealed class DisableableDataSourceUpdatesTracker
    {
        private readonly object _lock = new object();
        private readonly List<DisableableIDataSourceUpdates> _tracked = new List<DisableableIDataSourceUpdates>();

        public DisableableDataSourceUpdatesTracker()
        {
            // empty constructor
        }

        /// <summary>
        /// Wraps the provided <see cref="IDataSourceUpdatesV2"/> in a new instance that can be disabled by
        /// calling <see cref="DisablePreviouslyTracked"/>.
        /// </summary>
        /// <returns>a new proxy instance</returns>
        public IDataSourceUpdatesV2 WrapAndTrack(IDataSourceUpdatesV2 dsUpdates)
        {
            var dis = new DisableableIDataSourceUpdates(dsUpdates);
            lock (_lock)
            {
                _tracked.Add(dis);
            }

            return dis;
        }

        /// <summary>
        /// Disables all instances previously returned by <see cref="WrapAndTrack"/>.
        /// </summary>
        public void DisablePreviouslyTracked()
        {
            List<DisableableIDataSourceUpdates> toDisable;
            lock (_lock)
            {
                if (_tracked.Count == 0)
                {
                    return;
                }

                toDisable = new List<DisableableIDataSourceUpdates>(_tracked);
                _tracked.Clear();
            }

            foreach (var it in toDisable)
            {
                it.Disable();
            }
        }

        /// <summary>
        /// A proxy for <see cref="IDataSourceUpdatesV2"/> that can be disabled. When disabled,
        /// all calls to the proxy will be ignored.
        /// </summary>
        private sealed class DisableableIDataSourceUpdates : IDataSourceUpdatesV2
        {
            private readonly IDataSourceUpdatesV2 _updatesSink;
            private volatile bool _disabled;

            public DisableableIDataSourceUpdates(IDataSourceUpdatesV2 updatesSink)
            {
                _updatesSink = updatesSink ?? throw new ArgumentNullException(nameof(updatesSink));
            }

            /// <summary>
            /// Disables this proxy so that future calls are ignored.
            /// </summary>
            public void Disable()
            {
                _disabled = true;
            }

            private bool IsDisabled => _disabled;

            public IDataStoreStatusProvider DataStoreStatusProvider => _updatesSink.DataStoreStatusProvider;
            
            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                if (IsDisabled)
                {
                    return;
                }

                _updatesSink.UpdateStatus(newState, newError);
            }

            public bool Apply(ChangeSet<ItemDescriptor> changeSet)
            {
                return !IsDisabled && _updatesSink.Apply(changeSet);
            }
        }
    }
}


