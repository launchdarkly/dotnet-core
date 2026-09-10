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
        private const string ErrorContextMessage = "on polling request";
        private const string WillRetryMessage = "will retry at next scheduled poll interval";

        private readonly TaskExecutor _taskExecutor;
        private readonly TimeSpan _pollInterval;
        private readonly PollingStrategy _strategy;
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);
        private readonly TaskCompletionSource<bool> _initTask;
        private readonly Logger _log;
        private readonly CancellationTokenSource _canceller = new CancellationTokenSource();
        private bool _started;

        private bool _disposed = false;
        private readonly AtomicBoolean _shuttingDown = new AtomicBoolean(false);

        internal PollingDataSource(
            LdClientContext context,
            IFeatureRequestor featureRequestor,
            IDataSourceUpdates dataSourceUpdates,
            TimeSpan pollInterval,
            TimeSpan extendedInitialInterval
            )
        {
            _featureRequestor = featureRequestor;
            _dataSourceUpdates = dataSourceUpdates;
            _taskExecutor = context.TaskExecutor;
            _pollInterval = pollInterval;
            _strategy = new PollingStrategy(pollInterval, extendedInitialInterval);
            _initTask = new TaskCompletionSource<bool>();
            _log = context.Logger.SubLogger(LogNames.DataSourceSubLog);
        }

        public bool Initialized => _initialized.Get();

        public Task<bool> Start()
        {
            lock (this)
            {
                if (!_started)
                {
                    _started = true;
                    _log.Info("Starting LaunchDarkly polling with interval: {0} milliseconds",
                        _pollInterval.TotalMilliseconds);
                    ScheduleNext(TimeSpan.Zero);
                }
            }

            return _initTask.Task;
        }

        /// <summary>
        /// Schedules the next poll, unless the data source has been shut down.
        /// </summary>
        private void ScheduleNext(TimeSpan delay)
        {
            if (_canceller.IsCancellationRequested)
            {
                return;
            }
            _taskExecutor.ScheduleTask(delay, PollAsync, _canceller.Token);
        }

        /// <summary>
        /// Runs one poll and schedules the next one.
        /// </summary>
        /// <remarks>
        /// The gap is no longer constant: an unexpected failure slows polling down until two
        /// consecutive polls succeed. Rescheduling happens in a finally so that it survives any
        /// outcome, and each poll starts on a fresh stack because the executor dispatches it, so
        /// this does not accumulate depth despite being self-referential.
        /// </remarks>
        private async Task PollAsync()
        {
            try
            {
                await UpdateTaskAsync();
            }
            finally
            {
                ScheduleNext(_strategy.NextWait());
            }
        }

        private async Task UpdateTaskAsync()
        {
            _log.Info("Polling LaunchDarkly for feature flag updates");
            try
            {
                var dataAndHeaders = await _featureRequestor.GetAllDataAsync();
                if (dataAndHeaders is null || dataAndHeaders.DataSet is null)
                {
                    // This means it was cached, and alreadyInited was true
                    TryUpdateStatus(DataSourceState.Valid, null);
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
                _strategy.OnSuccess();
            }
            catch (UnsuccessfulResponseException ex)
            {
                var failureClass = HttpErrors.ClassifyAndLogHttpFailure(_log, ex.StatusCode,
                    ErrorContextMessage, WillRetryMessage);
                // Reported as recoverable in all cases. Data sources no longer give up on any HTTP
                // status.
                var errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(ex.StatusCode, true);
                TryUpdateStatus(DataSourceState.Interrupted, errorInfo);
                OnFailure(failureClass);
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
                TryUpdateStatus(DataSourceState.Interrupted, errorInfo);
                OnFailure(FailureClass.Normal);
            }
            catch (Exception ex)
            {
                Exception realEx = (ex is AggregateException ae) ? ae.Flatten() : ex;
                _log.Warn("Polling for feature flag updates failed: {0}", LogValues.ExceptionSummary(ex));
                _log.Debug(LogValues.ExceptionTrace(ex));
                var errorInfo = DataSourceStatus.ErrorInfo.FromException(realEx, true); // default to recoverable
                TryUpdateStatus(DataSourceState.Interrupted, errorInfo);
                OnFailure(HttpErrors.ClassifyTransportFailure(realEx));
            }
        }

        private void OnFailure(FailureClass failureClass)
        {
            if (_strategy.OnFailure(failureClass))
            {
                _log.Info("Classified failure as unexpected; engaging extended backoff.");
            }
        }

        void IDisposable.Dispose()
        {
            // dispose is currently overloaded with shutdown responsibility, we handle this first
            Shutdown(null);

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing) {
                // dispose managed resources if any
                _featureRequestor.Dispose();
                _canceller.Dispose();
            }

            _disposed = true;
        }

        /// <summary>
        /// Publishes a status update unless the data source is shutting down.
        /// </summary>
        /// <remarks>
        /// Work already in flight when shutdown begins keeps running -- a poll or stream read is
        /// not interrupted -- and typically fails against the resources disposal just closed. Left
        /// unguarded, that late failure publishes Interrupted over the terminal Off, so a disposed
        /// data source reports itself interrupted and status listeners see a spurious event after
        /// shutdown. Shutdown publishes Off directly rather than through this method.
        /// </remarks>
        private void TryUpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError)
        {
            if (_shuttingDown.Get())
            {
                return;
            }
            _dataSourceUpdates.UpdateStatus(newState, newError);
        }

        private void Shutdown(DataSourceStatus.ErrorInfo? errorInfo)
        {
            // Prevent concurrent shutdown calls - only allow the first call to proceed
            // GetAndSet returns the OLD value, so if it was already true, we return early
            if (_shuttingDown.GetAndSet(true)) return;

            _canceller.Cancel();
            _dataSourceUpdates.UpdateStatus(DataSourceState.Off, errorInfo);
            _initTask.TrySetResult(false);
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
