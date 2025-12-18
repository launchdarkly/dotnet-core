using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    internal sealed class PollingDataSource : IDataSource
    {
        private readonly IFeatureRequestor _featureRequestor;
        private readonly IDataSourceUpdates _dataSourceUpdates;
        private readonly TaskExecutor _taskExecutor;
        private readonly TimeSpan _pollInterval;
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);
        private readonly TaskCompletionSource<bool> _initTask;
        private readonly Logger _log;
        private CancellationTokenSource _canceller;

        internal PollingDataSource(
            LdClientContext context,
            IFeatureRequestor featureRequestor,
            IDataSourceUpdates dataSourceUpdates,
            TimeSpan pollInterval
            )
        {
            _featureRequestor = featureRequestor;
            _dataSourceUpdates = dataSourceUpdates;
            _taskExecutor = context.TaskExecutor;
            _pollInterval = pollInterval;
            _initTask = new TaskCompletionSource<bool>();
            _log = context.Logger.SubLogger(LogNames.DataSourceSubLog);
        }

        public bool Initialized => _initialized.Get();

        public Task<bool> Start()
        {
            lock (this)
            {
                if (_canceller == null) // means we already started
                {
                    _log.Info("Starting LaunchDarkly polling with interval: {0} milliseconds",
                        _pollInterval.TotalMilliseconds);
                    _canceller = _taskExecutor.StartRepeatingTask(TimeSpan.Zero,
                        _pollInterval, () => UpdateTaskAsync());
                }
            }

            return _initTask.Task;
        }

        private async Task UpdateTaskAsync()
        {
            _log.Info("Polling LaunchDarkly for feature flag updates");
            try
            {
                var dataAndHeaders = await _featureRequestor.GetAllDataAsync();
                if (dataAndHeaders.DataSet is null)
                {
                    // This means it was cached, and alreadyInited was true
                    _dataSourceUpdates.UpdateStatus(DataSourceState.Valid, null);
                }
                else
                {
                    if (InitWithHeaders(dataAndHeaders.DataSet.Value, dataAndHeaders.Headers)) // this also automatically sets the state to Valid
                    {
                        if (!_initialized.GetAndSet(true))
                        {
                            _initTask.SetResult(true);
                            _log.Info("First polling request successful");
                        }
                    }
                }
            }
            catch (UnsuccessfulResponseException ex)
            {
                var recoverable = HttpErrors.IsRecoverable(ex.StatusCode);
                var errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(ex.StatusCode, recoverable);

                if (errorInfo.Recoverable)
                {
                    _log.Warn(HttpErrors.ErrorMessage(ex.StatusCode, "polling request", "will retry"));
                    _dataSourceUpdates.UpdateStatus(DataSourceState.Interrupted, errorInfo);
                }
                else
                {
                    _log.Error(HttpErrors.ErrorMessage(ex.StatusCode, "polling request", ""));
                    try
                    {
                        // if client is initializing, make it stop waiting
                        _initTask.SetResult(false);
                    }
                    catch (InvalidOperationException)
                    {
                        // the task was already set - nothing more to do
                    }
                    Shutdown(errorInfo);
                }
            }
            catch (JsonException ex)
            {
                _log.Error("Polling request received malformed data: {0}", LogValues.ExceptionSummary(ex));
                var errorInfo = new DataSourceStatus.ErrorInfo
                {
                    Kind = DataSourceStatus.ErrorKind.InvalidData,
                    Message = ex.Message,
                    Time = DateTime.Now,
                    Recoverable = true
                };
                _dataSourceUpdates.UpdateStatus(DataSourceState.Interrupted, errorInfo);
            }
            catch (Exception ex)
            {
                Exception realEx = (ex is AggregateException ae) ? ae.Flatten() : ex;
                _log.Warn("Polling for feature flag updates failed: {0}", LogValues.ExceptionSummary(ex));
                _log.Debug(LogValues.ExceptionTrace(ex));
                var errorInfo = DataSourceStatus.ErrorInfo.FromException(realEx, true); // default to recoverable
                _dataSourceUpdates.UpdateStatus(DataSourceState.Interrupted, errorInfo);
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
                Shutdown(null);
            }
        }

        private void Shutdown(DataSourceStatus.ErrorInfo? errorInfo)
        {
            _canceller?.Cancel();
            _featureRequestor.Dispose();
            _dataSourceUpdates.UpdateStatus(DataSourceState.Off, errorInfo);
        }

        private bool InitWithHeaders(DataStoreTypes.FullDataSet<DataStoreTypes.ItemDescriptor> allData,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            if (_dataSourceUpdates is IDataSourceUpdatesHeaders dataSourceUpdatesHeaders)
            {
                return dataSourceUpdatesHeaders.InitWithHeaders(allData, headers);
            }

            return _dataSourceUpdates.Init(allData);
        }
    }
}
