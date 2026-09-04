using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LaunchDarkly.EventSource;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Server.Internal.DataSources.StreamProcessorEvents;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    internal class StreamingDataSource : IDataSource
    {
        // The read timeout for the stream is not the same read timeout that can be set in the SDK configuration.
        // It is a fixed value that is set to be slightly longer than the expected interval between heartbeats
        // from the LaunchDarkly streaming server. If this amount of time elapses with no new data, the connection
        // will be cycled.
        private static readonly TimeSpan LaunchDarklyStreamReadTimeout = TimeSpan.FromMinutes(5);

        private const String PUT = "put";
        private const String PATCH = "patch";
        private const String DELETE = "delete";

        private const string ErrorContextMessage = "in stream connection";
        private const string WillRetryMessage = "will retry";

        private readonly IDataSourceUpdates _dataSourceUpdates;
        private readonly HttpConfiguration _httpConfig;
        private readonly TimeSpan _initialReconnectDelay;
        private readonly TimeSpan _extendedInitialReconnectDelay;
        private readonly TimeSpan _extendedMaxRetryDelay;
        private readonly TaskCompletionSource<bool> _initTask;
        private readonly IDiagnosticStore _diagnosticStore;
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);
        private readonly Uri _streamUri;
        private readonly bool _storeStatusMonitoringEnabled;
        private readonly Logger _log;

        private readonly IEventSource _es;
        /// <summary>
        /// When the store enters a failed state, and we don't have "data source monitoring", we want to log
        /// a message that we are restarting the event source. We don't want to log this message on multiple
        /// sequential failures. This boolean is used to determine if the previous attempt to write also
        /// failed, and in which case we will not log.
        /// </summary>
        private volatile bool _lastStoreUpdateFailed = false;

        /// <summary>
        /// Gates the "engaging extended backoff" message so that a sustained outage logs it once
        /// rather than on every reconnection attempt.
        /// </summary>
        private volatile bool _loggedActivatedExtended = false;
        internal DateTime _esStarted; // exposed for testing
        private readonly Stopwatch _esTimer = new Stopwatch();

        private IEnumerable<KeyValuePair<string, IEnumerable<string>>> _headers;

        private bool _disposed = false;
        private readonly AtomicBoolean _shuttingDown = new AtomicBoolean(false);

        internal delegate IEventSource EventSourceCreator(Uri streamUri,
            HttpConfiguration httpConfig);

        internal StreamingDataSource(
            LdClientContext context,
            IDataSourceUpdates dataSourceUpdates,
            Uri baseUri,
            TimeSpan initialReconnectDelay,
            TimeSpan extendedInitialReconnectDelay,
            TimeSpan extendedMaxRetryDelay,
            EventSourceCreator eventSourceCreator = null
            )
        {
            _log = context.Logger.SubLogger(LogNames.DataSourceSubLog);
            _log.Info("Connecting to LaunchDarkly stream");

            _dataSourceUpdates = dataSourceUpdates;
            _httpConfig = context.Http;
            _initialReconnectDelay = initialReconnectDelay;
            _extendedInitialReconnectDelay = extendedInitialReconnectDelay;
            _extendedMaxRetryDelay = extendedMaxRetryDelay;
            _diagnosticStore = context.DiagnosticStore;
            _initTask = new TaskCompletionSource<bool>();
            _streamUri = baseUri.AddPath(StandardEndpoints.StreamingRequestPath);

            _storeStatusMonitoringEnabled = _dataSourceUpdates.DataStoreStatusProvider.StatusMonitoringEnabled;
            if (_storeStatusMonitoringEnabled)
            {
                _dataSourceUpdates.DataStoreStatusProvider.StatusChanged += OnDataStoreStatusChanged;
            }

            var esc = eventSourceCreator ?? CreateEventSource;
            _es = esc(_streamUri, _httpConfig);
            _es.MessageReceived += OnMessage;
            _es.Error += OnError;
            _es.Opened += OnOpen;
        }

        #region IDataSource

        public bool Initialized => _initialized.Get();

        public Task<bool> Start()
        {
            Task.Run(() => {
                _esStarted = DateTime.UtcNow;
                _esTimer.Restart();
                return _es.StartAsync();
            });

            return _initTask.Task;
        }

        #endregion

        #region IDisposable

        public void Dispose()
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

            _es.Close();
            if (_storeStatusMonitoringEnabled)
            {
                _dataSourceUpdates.DataStoreStatusProvider.StatusChanged -= OnDataStoreStatusChanged;
            }
            _dataSourceUpdates.UpdateStatus(DataSourceState.Off, errorInfo);
            _initTask.TrySetResult(false);
        }

        #endregion

        private IEventSource CreateEventSource(Uri uri, HttpConfiguration httpConfig)
        {
            var configBuilder = EventSource.Configuration.Builder(uri)
                .Method(HttpMethod.Get)
                .HttpMessageHandler(httpConfig.HttpProperties.NewHttpMessageHandler())
                .ResponseStartTimeout(httpConfig.ResponseStartTimeout)
                .InitialRetryDelay(_initialReconnectDelay)
                .ReadTimeout(LaunchDarklyStreamReadTimeout)
                .RequestHeaders(httpConfig.DefaultHeaders.ToDictionary(kv => kv.Key, kv => kv.Value))
                .PreferDataAsUtf8Bytes(true) // See StreamProcessorEvents
                .Logger(_log);
            return new EventSource.EventSource(configBuilder.Build());
        }

        private void RecordStreamInit(bool failed)
        {
            if (_diagnosticStore != null)
            {
                var duration = _esTimer.Elapsed;
                var streamStarted = _esStarted;
                _esStarted = DateTime.UtcNow;
                _esTimer.Restart();
                _diagnosticStore.AddStreamInit(streamStarted, duration, failed);
            }
        }

        private void OnOpen(object sender, EventSource.StateChangedEventArgs e)
        {
            _headers = e.Headers;
            _log.Debug("EventSource Opened");
            RecordStreamInit(false);
        }

        private void OnMessage(object sender, EventSource.MessageReceivedEventArgs e)
        {
            try
            {
                HandleMessage(e.EventName, e.Message.DataUtf8Bytes.Data);
                // The way the PreferDataAsUtf8Bytes option works in EventSource is that if the
                // stream really is using UTF-8 encoding, the event data is passed to us directly
                // in Message.DataUtf8Bytes as a byte array and does not need to be converted to
                // a string. If the stream is for some reason using a different encoding, then
                // EventSource reads the data as a string (automatically converted by .NET from
                // whatever the encoding was), and then calling Message.DataUtf8Bytes converts
                // that to UTF-8 bytes.
            }
            catch (JsonException ex)
            {
                _log.Error("LaunchDarkly service request failed or received invalid data: {0}",
                    LogValues.ExceptionSummary(ex));

                var errorInfo = new DataSourceStatus.ErrorInfo
                {
                    Kind = DataSourceStatus.ErrorKind.InvalidData,
                    Message = ex.Message,
                    Time = DateTime.Now,
                    Recoverable = true
                };
                TryUpdateStatus(DataSourceState.Interrupted, errorInfo);

                _es.Restart(false);
            }
            catch (StreamStoreException ex)
            {
                var errorInfo = new DataSourceStatus.ErrorInfo
                {
                    Kind = DataSourceStatus.ErrorKind.StoreError,
                    Message = (ex.InnerException ?? ex).Message,
                    Time = DateTime.Now,
                    Recoverable = true
                };
                TryUpdateStatus(DataSourceState.Interrupted, errorInfo);
                if (!_storeStatusMonitoringEnabled)
                {
                    if (!_lastStoreUpdateFailed)
                    {
                        _log.Warn("Restarting stream to ensure that we have the latest data");
                    }
                    _es.Restart(false);
                }
                _lastStoreUpdateFailed = true;
            }
            catch (Exception ex)
            {
                LogHelpers.LogException(_log, "Unexpected error in stream processing", ex);
                _es.Restart(false);
            }
        }

        private void OnError(object sender, EventSource.ExceptionEventArgs e)
        {
            var ex = e.Exception;
            DataSourceStatus.ErrorInfo errorInfo;

            FailureClass failureClass;

            if (ex is EventSourceServiceUnsuccessfulResponseException respEx)
            {
                int status = respEx.StatusCode;
                failureClass = HttpErrors.ClassifyAndLogHttpFailure(_log, status, ErrorContextMessage,
                    WillRetryMessage);
                errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(status, true);
                RecordStreamInit(true);
            }
            else
            {
                failureClass = HttpErrors.ClassifyAndLogTransportFailure(_log, ex,
                    ErrorContextMessage, WillRetryMessage);
                errorInfo = DataSourceStatus.ErrorInfo.FromException(ex, true);
                _log.Debug(LogValues.ExceptionTrace(ex));
            }

            if (failureClass == FailureClass.Unexpected)
            {
                // No failure permanently stops the stream. A failure that will not clear on its
                // own gets a slower reconnection cadence instead, and the underlying library
                // reverts to the configured bounds automatically after the reset threshold of
                // continuous connection.
                _es.SetTemporaryRetryDelayBounds(_extendedInitialReconnectDelay,
                    _extendedMaxRetryDelay);
                if (!_loggedActivatedExtended)
                {
                    _log.Info("Classified failure as unexpected; engaging extended backoff.");
                    _loggedActivatedExtended = true;
                }
            }

            // All HTTP errors are treated as non-permanent, so we report it as Interrupted.
            TryUpdateStatus(DataSourceState.Interrupted, errorInfo);
        }

        private bool InitWithHeaders(FullDataSet<ItemDescriptor> allData,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            if (_dataSourceUpdates is IDataSourceUpdatesHeaders dataSourceUpdatesHeaders)
            {
                return dataSourceUpdatesHeaders.InitWithHeaders(allData, headers);
            }

            return _dataSourceUpdates.Init(allData);
        }

        private void HandleMessage(string messageType, byte[] messageData)
        {
            switch (messageType)
            {
                case PUT:
                    var putData = ParsePutData(messageData);
                    if (!InitWithHeaders(putData.Data, _headers)) // this also automatically sets the state to Valid
                    {
                        throw new StreamStoreException("failed to write full data set to data store");
                    }
                    _lastStoreUpdateFailed = false;
                    if (!_initialized.GetAndSet(true))
                    {
                        _initTask.TrySetResult(true);
                        _log.Info("LaunchDarkly streaming is active");
                    }
                    break;

                case PATCH:
                    PatchData patchData = ParsePatchData(messageData);
                    if (patchData.Kind is null)
                    {
                        _log.Warn("Received patch event with unknown path");
                    }
                    else
                    {
                        if (!_dataSourceUpdates.Upsert(patchData.Kind, patchData.Key, patchData.Item))
                        {
                            throw new StreamStoreException(string.Format("failed to update \"{0}\" ({1}) in data store",
                                patchData.Key, patchData.Kind.Name));
                        }
                    }
                    _lastStoreUpdateFailed = false;
                    break;

                case DELETE:
                    DeleteData deleteData = ParseDeleteData(messageData);
                    if (deleteData.Kind is null)
                    {
                        _log.Warn("Received patch event with unknown path");
                    }
                    else
                    {
                        var tombstone = new ItemDescriptor(deleteData.Version, null);
                        if (!_dataSourceUpdates.Upsert(deleteData.Kind, deleteData.Key, tombstone))
                        {
                            throw new StreamStoreException(string.Format("failed to delete \"{0}\" ({1}) in data store",
                                deleteData.Key, deleteData.Kind.Name));
                        }
                        _lastStoreUpdateFailed = false;
                    }
                    break;
            }
        }

        private void OnDataStoreStatusChanged(object sender, DataStoreStatus newStatus)
        {
            if (newStatus.Available && newStatus.RefreshNeeded)
            {
                // The store has just transitioned from unavailable to available, and we can't guarantee that
                // all of the latest data got cached, so let's restart the stream to refresh all the data.
                _log.Warn("Restarting stream to refresh data after data store outage");
                _es.Restart(false);
            }
        }

        private sealed class StreamStoreException : Exception
        {
            public StreamStoreException(string message) : base(message) { }
        }
    }
}
