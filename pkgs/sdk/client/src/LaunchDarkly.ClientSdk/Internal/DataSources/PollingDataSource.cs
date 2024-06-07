using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    internal sealed class PollingDataSource : IDataSource
    {
        private readonly IFeatureFlagRequestor _featureFlagRequestor;
        private readonly IDataSourceUpdateSink _updateSink;
        private readonly Context _context;
        private readonly TimeSpan _pollingInterval;
        private readonly TimeSpan _initialDelay;
        private readonly Logger _log;
        private readonly TaskExecutor _taskExecutor;
        private readonly TaskCompletionSource<bool> _startTask;
        private volatile CancellationTokenSource _canceller;
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);

        internal PollingDataSource(
            IDataSourceUpdateSink updateSink,
            Context context,
            IFeatureFlagRequestor featureFlagRequestor,
            TimeSpan pollingInterval,
            TimeSpan initialDelay,
            TaskExecutor taskExecutor,
            Logger log)
        {
            this._featureFlagRequestor = featureFlagRequestor;
            this._updateSink = updateSink;
            this._context = context;
            this._pollingInterval = pollingInterval;
            this._initialDelay = initialDelay;
            this._taskExecutor = taskExecutor;
            this._log = log;
            _startTask = new TaskCompletionSource<bool>();
        }

        public Task<bool> Start()
        {
            if (_pollingInterval.Equals(TimeSpan.Zero))
                throw new Exception("Timespan for polling can't be zero");

            if (_initialDelay > TimeSpan.Zero)
            {
                _log.Info("Starting LaunchDarkly PollingProcessor with interval: {0} (waiting {1} first)", _pollingInterval, _initialDelay);
            }
            else
            {
                _log.Info("Starting LaunchDarkly PollingProcessor with interval: {0}", _pollingInterval);
            }

            _canceller = _taskExecutor.StartRepeatingTask(_initialDelay, _pollingInterval, UpdateTaskAsync);
            return _startTask.Task;
        }

        public bool Initialized => _initialized.Get();

        private async Task UpdateTaskAsync()
        {
            try
            {
                var response = await _featureFlagRequestor.FeatureFlagsAsync();
                if (response.statusCode == 200)
                {
                    var flagsAsJsonString = response.jsonResponse;
                    var allData = DataModelSerialization.DeserializeV1Schema(flagsAsJsonString);
                    _updateSink.Init(_context, allData);

                    if (_initialized.GetAndSet(true) == false)
                    {
                        _startTask.SetResult(true);
                        _log.Info("Initialized LaunchDarkly Polling Processor.");
                    }
                }
            }
            catch (UnsuccessfulResponseException ex)
            {
                var errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(ex.StatusCode);

                if (HttpErrors.IsRecoverable(ex.StatusCode))
                {
                    _log.Warn(HttpErrors.ErrorMessage(ex.StatusCode, "polling request", "will retry"));
                    _updateSink.UpdateStatus(DataSourceState.Interrupted, errorInfo);
                }
                else
                {
                    _log.Error(HttpErrors.ErrorMessage(ex.StatusCode, "polling request", ""));
                    _updateSink.UpdateStatus(DataSourceState.Shutdown, errorInfo);

                    // if client is initializing, make it stop waiting
                    _startTask.TrySetResult(false);

                    ((IDisposable)this).Dispose();
                }
            }
            catch (InvalidDataException ex)
            {
                _log.Error("Polling request received malformed data: {0}", LogValues.ExceptionSummary(ex));
                _updateSink.UpdateStatus(DataSourceState.Interrupted,
                    new DataSourceStatus.ErrorInfo
                    {
                        Kind = DataSourceStatus.ErrorKind.InvalidData,
                        Time = DateTime.Now
                    });
            }
            catch (Exception ex)
            {
                Exception realEx = (ex is AggregateException ae) ? ae.Flatten() : ex;
                _log.Warn("Polling for feature flag updates failed: {0}", LogValues.ExceptionSummary(realEx));
                _log.Debug(LogValues.ExceptionTrace(realEx));
                _updateSink.UpdateStatus(DataSourceState.Interrupted,
                    DataSourceStatus.ErrorInfo.FromException(realEx));
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
                _featureFlagRequestor.Dispose();
            }
        }
    }
}
