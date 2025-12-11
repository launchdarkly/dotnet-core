using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// An <see cref="IDataSourceUpdates"/> implementation that decorates an underlying
    /// <see cref="IDataSourceUpdates"/> and sanitizes status updates.
    /// </summary>
    /// <remarks>
    /// This wrapper ensures the following:
    /// <list type="bullet">
    /// <item><description>Does not report <see cref="DataSourceState.Initializing"/> except the first time it is seen.</description></item>
    /// <item><description>Maps underlying <see cref="DataSourceState.Off"/> to <see cref="DataSourceState.Interrupted"/>.</description></item>
    /// <item><description>Does not report the same combination of state and error twice in a row.</description></item>
    /// </list>
    /// </remarks>
    internal sealed class DataSourceUpdatesSanitizer : IDataSourceUpdates, ITransactionalDataSourceUpdates
    {
        private readonly IDataSourceUpdates _inner;
        private readonly object _lock = new object();

        private bool _alreadyReportedInitializing;
        private DataSourceState? _lastState;
        private DataSourceStatus.ErrorInfo? _lastError;

        /// <summary>
        /// Creates a new <see cref="DataSourceUpdatesSanitizer"/>.
        /// </summary>
        /// <param name="inner">the underlying updates sink to delegate to</param>
        public DataSourceUpdatesSanitizer(IDataSourceUpdates inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// The underlying <see cref="IDataStoreStatusProvider"/> from the decorated sink.
        /// </summary>
        public IDataStoreStatusProvider DataStoreStatusProvider => _inner.DataStoreStatusProvider;

        public bool Init(FullDataSet<ItemDescriptor> allData)
        {
            return _inner.Init(allData);
        }

        public bool Upsert(DataKind kind, string key, ItemDescriptor item)
        {
            return _inner.Upsert(kind, key, item);
        }

        public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
        {
            lock (_lock)
            {
                var sanitized = newState;

                // Map any future Off state to Interrupted
                if (sanitized == DataSourceState.Off)
                {
                    sanitized = DataSourceState.Interrupted;
                }

                // Don't report the same combination of values twice in a row.
                if (sanitized == _lastState && Nullable.Equals(newError, _lastError))
                {
                    return;
                }

                if (sanitized == DataSourceState.Initializing)
                {
                    // Don't report initializing again if that has already been reported.
                    if (_alreadyReportedInitializing)
                    {
                        return;
                    }

                    _alreadyReportedInitializing = true;
                }

                _lastState = sanitized;
                _lastError = newError;

                _inner.UpdateStatus(sanitized, newError);
            }
        }

        public bool Apply(ChangeSet<ItemDescriptor> changeSet)
        {
            return ((ITransactionalDataSourceUpdates)_inner).Apply(changeSet);
        }
    }
}


