using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Server.Interfaces;
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

        private CancellationTokenSource _canceler;
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

            _log.Debug("Created LaunchDarkly FDv2 polling data source");
        }

        public bool Initialized => _initialized.Get();

        public Task<bool> Start()
        {
            lock (this)
            {
                // If we have a canceler, then the source has already been started.
                if (_canceler != null) return _initTask.Task;

                _log.Info("Starting LaunchDarkly FDv2 polling with interval: {0} milliseconds",
                    _pollInterval.TotalMilliseconds);
                _canceler = _taskExecutor.StartRepeatingTask(TimeSpan.Zero,
                    _pollInterval, UpdateTaskAsync);
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

                if (response == null)
                {
                    _log.Debug("Polling response not modified, skipping processing");
                    return;
                }

                if (response.Value.Headers != null)
                {
                    _environmentId = response.Value.Headers.FirstOrDefault(item =>
                            item.Key.ToLower() == HeaderConstants.EnvironmentId).Value
                        ?.FirstOrDefault();
                }

                ProcessPollingResponse(response.Value);
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
                var realEx = (ex is AggregateException ae) ? ae.Flatten() : ex;
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
            switch (action)
            {
                case FDv2ActionChangeset changesetAction:
                    ProcessChangeSet(changesetAction.Changeset);
                    break;
                case FDv2ActionError errorAction:
                    _log.Error("FDv2 error event: {0} - {1}", errorAction.Id, errorAction.Reason);
                    break;
                case FDv2ActionGoodbye goodbyeAction:
                    _log.Info("FDv2 server disconnecting: {0}", goodbyeAction.Reason);
                    break;
                case FDv2ActionInternalError internalErrorAction:
                    _log.Error("FDv2 protocol error ({0}): {1}", internalErrorAction.ErrorType,
                        internalErrorAction.Message);
                    break;
                case FDv2ActionNone _:
                    // No action needed
                    break;
                default:
                    // Represents an implementation error. Actions expanded without the handling
                    // being expanded.
                    _log.Error("Unhandled FDv2 Protocol Action.");
                    break;
            }
        }

        private void ProcessChangeSet(FDv2ChangeSet fdv2ChangeSet)
        {
            if (!(_dataSourceUpdates is ITransactionalDataSourceUpdates transactionalDataSourceUpdates))
                throw new InvalidOperationException("Cannot apply updates to non-transactional data source");

            var dataStoreChangeSet = FDv2ChangeSetTranslator.ToChangeSet(fdv2ChangeSet, _log, _environmentId);

            // If the update fails, then we wait until the next poll and try again.
            // This is different from a streaming data source, which will need to re-start to get an initial
            // payload.
            if (!transactionalDataSourceUpdates.Apply(dataStoreChangeSet)) return;
            
            // Only mark as initialized after successfully applying a changeset
            if (_initialized.GetAndSet(true)) return;
            _initTask.SetResult(true);
            _log.Info("First polling request successful");
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;

            Shutdown();
        }

        private void Shutdown()
        {
            _canceler?.Cancel();
            _requestor.Dispose();
        }
    }
}
