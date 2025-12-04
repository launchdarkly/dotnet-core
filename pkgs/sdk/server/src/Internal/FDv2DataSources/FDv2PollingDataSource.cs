using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    internal sealed class FDv2PollingDataSource : IDataSource
    {
        internal delegate Selector SelectorSource();

        private readonly IFDv2PollingRequestor _requestor;
        private readonly IDataSourceUpdates _dataSourceUpdates;
        private readonly TaskExecutor _taskExecutor;
        private readonly TimeSpan _pollInterval;
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);
        private readonly TaskCompletionSource<bool> _initTask;
        private readonly Logger _log;
        private readonly FDv2ProtocolHandler _protocolHandler = new FDv2ProtocolHandler();
        private readonly object _protocolLock = new object();
        private readonly SelectorSource _selectorSource;
        private readonly bool _storeStatusMonitoringEnabled;
        private readonly AtomicBoolean _lastStoreUpdateFailed = new AtomicBoolean(false);

        private CancellationTokenSource _canceller;
        private string _environmentId;

        internal FDv2PollingDataSource(
            LdClientContext context,
            IDataSourceUpdates dataSourceUpdates,
            IFDv2PollingRequestor requestor,
            TimeSpan pollInterval,
            SelectorSource selectorSource
        )
        {
            _requestor = requestor;
            _dataSourceUpdates = dataSourceUpdates;
            _taskExecutor = context.TaskExecutor;
            _pollInterval = pollInterval;
            _selectorSource = selectorSource;
            _initTask = new TaskCompletionSource<bool>();
            _log = context.Logger.SubLogger(LogNames.FDv2DataSourceSubLog);

            _storeStatusMonitoringEnabled = _dataSourceUpdates.DataStoreStatusProvider.StatusMonitoringEnabled;
            if (_storeStatusMonitoringEnabled)
            {
                _dataSourceUpdates.DataStoreStatusProvider.StatusChanged += OnDataStoreStatusChanged;
            }

            _log.Debug("Created LaunchDarkly FDv2 polling data source");
        }

        public bool Initialized => _initialized.Get();

        public Task<bool> Start()
        {
            lock (this)
            {
                if (_canceller == null)
                {
                    _log.Info("Starting LaunchDarkly FDv2 polling with interval: {0} milliseconds",
                        _pollInterval.TotalMilliseconds);
                    _canceller = _taskExecutor.StartRepeatingTask(TimeSpan.Zero,
                        _pollInterval, () => UpdateTaskAsync());
                }
            }

            return _initTask.Task;
        }

        private async Task UpdateTaskAsync()
        {
            _log.Debug("Polling LaunchDarkly for feature flag updates");
            try
            {
                var selector = _selectorSource();
                var response = await _requestor.PollingRequestAsync(selector);

                if (response.Headers != null)
                {
                    _environmentId = response.Headers.FirstOrDefault(item =>
                            item.Key.ToLower() == HeaderConstants.EnvironmentId).Value
                        ?.FirstOrDefault();
                }

                ProcessPollingResponse(response);

                if (!_initialized.GetAndSet(true))
                {
                    _initTask.SetResult(true);
                    _log.Info("First polling request successful");
                }
            }
            catch (UnsuccessfulResponseException ex)
            {
                var errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(ex.StatusCode);

                if (HttpErrors.IsRecoverable(ex.StatusCode))
                {
                    _log.Warn(HttpErrors.ErrorMessage(ex.StatusCode, "polling request", "will retry"));
                    _dataSourceUpdates.UpdateStatus(DataSourceState.Interrupted, errorInfo);
                }
                else
                {
                    _log.Error(HttpErrors.ErrorMessage(ex.StatusCode, "polling request", ""));
                    _dataSourceUpdates.UpdateStatus(DataSourceState.Off, errorInfo);
                    try
                    {
                        _initTask.SetResult(true);
                    }
                    catch (InvalidOperationException)
                    {
                        // the task was already set - nothing more to do
                    }
                    ((IDisposable)this).Dispose();
                }
            }
            catch (JsonException ex)
            {
                _log.Error("Polling request received malformed data: {0}", LogValues.ExceptionSummary(ex));
                _dataSourceUpdates.UpdateStatus(DataSourceState.Interrupted,
                    new DataSourceStatus.ErrorInfo
                    {
                        Kind = DataSourceStatus.ErrorKind.InvalidData,
                        Time = DateTime.Now
                    });
            }
            catch (Exception ex)
            {
                Exception realEx = (ex is AggregateException ae) ? ae.Flatten() : ex;
                _log.Warn("Polling for feature flag updates failed: {0}", LogValues.ExceptionSummary(ex));
                _log.Debug(LogValues.ExceptionTrace(ex));
                _dataSourceUpdates.UpdateStatus(DataSourceState.Interrupted,
                    DataSourceStatus.ErrorInfo.FromException(realEx));
            }
        }

        private void ProcessPollingResponse(FDv2PollingResponse response)
        {
            lock (_protocolLock)
            {
                _protocolHandler.Reset();

                foreach (var evt in response.Events)
                {
                    var action = _protocolHandler.HandleEvent(evt);
                    ProcessProtocolAction(action);
                }
            }
        }

        private void ProcessProtocolAction(IFDv2ProtocolAction action)
        {
            switch (action.Action)
            {
                case FDv2ProtocolActionType.Changeset:
                    var changeSetAction = action as FDv2ActionChangeset;
                    ProcessChangeSet(changeSetAction.Changeset);
                    break;
                case FDv2ProtocolActionType.Error:
                    var errorAction = action as FDv2ActionError;
                    _log.Error("FDv2 error event: {0} - {1}", errorAction.Id, errorAction.Reason);
                    break;
                case FDv2ProtocolActionType.Goodbye:
                    var goodbyeAction = action as FDv2ActionGoodbye;
                    _log.Info("FDv2 server disconnecting: {0}", goodbyeAction.Reason);
                    break;
                case FDv2ProtocolActionType.InternalError:
                    var internalErrorAction = action as FDv2ActionInternalError;
                    _log.Error("FDv2 protocol error ({0}): {1}", internalErrorAction.ErrorType,
                        internalErrorAction.Message);
                    break;
                case FDv2ProtocolActionType.None:
                    // No action needed
                    break;
            }
        }

        private void ProcessChangeSet(FDv2ChangeSet fdv2ChangeSet)
        {
            if (!(_dataSourceUpdates is ITransactionalDataSourceUpdates transactionalDataSourceUpdates))
                throw new InvalidOperationException("Cannot apply updates to non-transactional data source");

            var dataStoreChangeSet = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, _log, _environmentId);

            if (!transactionalDataSourceUpdates.Apply(dataStoreChangeSet))
            {
                if (!_storeStatusMonitoringEnabled)
                {
                    if (_lastStoreUpdateFailed.GetAndSet(true) == false)
                    {
                        _log.Warn("Failed to apply changeset to data store. Will retry on next poll.");
                    }
                }
            }
            else
            {
                _lastStoreUpdateFailed.GetAndSet(false);
            }
        }

        private void OnDataStoreStatusChanged(object sender, DataStoreStatus status)
        {
            if (status.Available)
            {
                _log.Warn("Data store is available again");
            }

            if (_initialized.Get())
            {
                var newState = status.Available ? DataSourceState.Valid : DataSourceState.Interrupted;
                var newError = status.Available ? (DataSourceStatus.ErrorInfo?)null :
                    new DataSourceStatus.ErrorInfo
                    {
                        Kind = DataSourceStatus.ErrorKind.StoreError,
                        Time = DateTime.Now
                    };
                _dataSourceUpdates.UpdateStatus(newState, newError);
            }
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _canceller?.Cancel();
                _requestor.Dispose();

                if (_storeStatusMonitoringEnabled)
                {
                    _dataSourceUpdates.DataStoreStatusProvider.StatusChanged -= OnDataStoreStatusChanged;
                }
            }
        }
    }
}