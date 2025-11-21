using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Provides instances of <see cref="IDataSourceUpdates"/> that can be disabled when necessary.
    /// Instances handed out forward calls to the updates sink provided at construction.
    /// If <see cref="DisableExistingProxies"/> is called, existing instances will ignore all
    /// future calls.
    /// </summary>
    internal sealed class DataSourceUpdatesProxyTracker
    {
        private readonly IDataSourceUpdates _updatesSink;
        private readonly object _lock = new object();
        private readonly List<DisableableIDataSourceUpdates> _proxies = new List<DisableableIDataSourceUpdates>();

        public DataSourceUpdatesProxyTracker(IDataSourceUpdates updatesSink)
        {
            _updatesSink = updatesSink ?? throw new ArgumentNullException(nameof(updatesSink));
        }

        /// <summary>
        /// Creates a new proxy that forwards calls to the underlying
        /// <see cref="IDataSourceUpdates"/> until disabled.
        /// </summary>
        /// <returns>a new proxy instance</returns>
        public IDataSourceUpdates NewProxy()
        {
            var proxy = new DisableableIDataSourceUpdates(_updatesSink);
            lock (_lock)
            {
                _proxies.Add(proxy);
            }

            return proxy;
        }

        /// <summary>
        /// Disables all existing proxy instances created by this object. After this
        /// is called, any further calls to those proxies will be ignored.
        /// </summary>
        public void DisableExistingProxies()
        {
            List<DisableableIDataSourceUpdates> proxiesToDisable;
            lock (_lock)
            {
                if (_proxies.Count == 0)
                {
                    return;
                }

                proxiesToDisable = new List<DisableableIDataSourceUpdates>(_proxies);
                _proxies.Clear();
            }

            foreach (var proxy in proxiesToDisable)
            {
                proxy.Disable();
            }
        }

        /// <summary>
        /// A proxy for <see cref="IDataSourceUpdates"/> that can be disabled. When disabled,
        /// all calls to the proxy will be ignored.
        /// </summary>
        private sealed class DisableableIDataSourceUpdates : IDataSourceUpdates, IDataSourceUpdatesHeaders
        {
            private readonly IDataSourceUpdates _updatesSink;
            private volatile bool _disabled;

            public DisableableIDataSourceUpdates(IDataSourceUpdates updatesSink)
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

            public bool Init(FullDataSet<ItemDescriptor> allData)
            {
                if (IsDisabled)
                {
                    return false;
                }

                return _updatesSink.Init(allData);
            }

            public bool Upsert(DataKind kind, string key, ItemDescriptor item)
            {
                if (IsDisabled)
                {
                    return false;
                }

                return _updatesSink.Upsert(kind, key, item);
            }

            public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
            {
                if (IsDisabled)
                {
                    return;
                }

                _updatesSink.UpdateStatus(newState, newError);
            }

            public bool InitWithHeaders(
                FullDataSet<ItemDescriptor> allData,
                IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers
                )
            {
                if (IsDisabled)
                {
                    return false;
                }

                if (_updatesSink is IDataSourceUpdatesHeaders headersSink)
                {
                    return headersSink.InitWithHeaders(allData, headers);
                }

                return _updatesSink.Init(allData);
            }
        }
    }
}


