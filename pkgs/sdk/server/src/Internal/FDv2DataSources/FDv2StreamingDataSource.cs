using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LaunchDarkly.EventSource;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    internal sealed class FDv2StreamingDataSource : IDataSource
    {
        internal delegate Selector SelectorSource();

        internal delegate IEventSource EventSourceCreator(Uri streamUri,
            HttpConfiguration httpConfig);

        // The read timeout for the stream is different from the read timeout that can be set in the SDK configuration.
        // It is a fixed value that is set to be slightly longer than the expected interval between heartbeats
        // from the LaunchDarkly streaming server. If this amount of time elapses with no new data, the connection
        // will be cycled.
        private static readonly TimeSpan LaunchDarklyStreamReadTimeout = TimeSpan.FromMinutes(5);

        private readonly IEventSource _es;
        private readonly TaskCompletionSource<bool> _initTask = new TaskCompletionSource<bool>();
        private DateTime _esStarted;
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);
        private readonly FDv2ProtocolHandler _protocolHandler = new FDv2ProtocolHandler();
        private readonly object _protocolLock = new object();

        private readonly IDiagnosticStore _diagnosticStore;
        private readonly IDataSourceUpdates _dataSourceUpdates;
        private readonly ITransactionalDataSourceUpdates _transactionalDataSourceUpdates;

        private readonly TimeSpan _initialReconnectDelay;
        private readonly Logger _log;
        private readonly bool _storeStatusMonitoringEnabled;

        private readonly SelectorSource _selectorSource;

        private string _environmentId;

        /// <summary>
        /// When the store enters a failed state, and we don't have "data source monitoring", we want to log
        /// a message that we are restarting the event source. We don't want to log this message on multiple
        /// sequential failures. This boolean is used to determine if the previous attempt to write also
        /// failed, and in which case we will not log.
        /// </summary>
        private readonly AtomicBoolean _lastStoreUpdateFailed = new AtomicBoolean(false);

        internal FDv2StreamingDataSource(
            LdClientContext context,
            IDataSourceUpdates dataSourceUpdates,
            Uri baseUri,
            TimeSpan initialReconnectDelay,
            SelectorSource selectorSource,
            EventSourceCreator eventSourceCreator = null
        )
        {
            _log = context.Logger.SubLogger(LogNames.FDv2DataSourceSubLog);
            _log.Debug("Created LaunchDarkly streaming data source");

            if (dataSourceUpdates is ITransactionalDataSourceUpdates transactionalDataSourceUpdates)
            {
                _transactionalDataSourceUpdates = transactionalDataSourceUpdates;
            }
            else
            {
                throw new InvalidOperationException("dataSourceUpdates must be ITransactionalDataSourceUpdates");
            }

            _dataSourceUpdates = dataSourceUpdates;

            _initialReconnectDelay = initialReconnectDelay;
            _diagnosticStore = context.DiagnosticStore;
            _selectorSource = selectorSource;
            // The event source creator is primarily provided for testing.
            var esc = eventSourceCreator ?? CreateEventSource;

            _storeStatusMonitoringEnabled = _dataSourceUpdates.DataStoreStatusProvider.StatusMonitoringEnabled;
            if (_storeStatusMonitoringEnabled)
            {
                _dataSourceUpdates.DataStoreStatusProvider.StatusChanged += OnDataStoreStatusChanged;
            }

            // The query parameters will be handled by the event source request
            // modifier. The modifier is called during initial connection and reconnections.
            var streamUri = baseUri.AddPath(StandardEndpoints.FDv2StreamingRequestPath);
            _es = esc(streamUri, context.Http);
            _es.MessageReceived += OnMessage;
            _es.Error += OnError;
            _es.Opened += OnOpen;
        }

        public void Dispose()
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
            _es.Close();
            if (_storeStatusMonitoringEnabled)
            {
                _dataSourceUpdates.DataStoreStatusProvider.StatusChanged -= OnDataStoreStatusChanged;
            }
        }

        public Task<bool> Start()
        {
            _log.Info("Connecting to LaunchDarkly stream");
            Task.Run(() =>
            {
                _esStarted = DateTime.Now;
                return _es.StartAsync();
            });

            return _initTask.Task;
        }

        public bool Initialized => _initialized.Get();

        private void HandleJsonError(string message)
        {
            _log.Error("LaunchDarkly service request failed or received invalid data: {0}", message);

            var errorInfo = new DataSourceStatus.ErrorInfo
            {
                Kind = DataSourceStatus.ErrorKind.InvalidData,
                Message = message,
                Time = DateTime.Now
            };
            _dataSourceUpdates.UpdateStatus(DataSourceState.Interrupted, errorInfo);

            _es.Restart(false);
        }

        private void HandleStoreError(string message)
        {
            var errorInfo = new DataSourceStatus.ErrorInfo
            {
                Kind = DataSourceStatus.ErrorKind.StoreError,
                Message = message,
                Time = DateTime.Now
            };
            _dataSourceUpdates.UpdateStatus(DataSourceState.Interrupted, errorInfo);
            if (_storeStatusMonitoringEnabled) return;

            // If the value before the set was false, then we log the warning.
            // We want to only log this once per outage.
            if (!_lastStoreUpdateFailed.GetAndSet(true))
            {
                _log.Warn("Restarting stream to ensure that we have the latest data");
            }

            _es.Restart(false);
        }

        private void OnMessage(object sender, MessageReceivedEventArgs e)
        {
            var parsed = FDv2Event.TryDeserializeFromJsonString(
                e.Message.Name,
                e.Message.Data,
                out var evt,
                out var error);

            if (!parsed)
            {
                HandleJsonError(error);
                return;
            }

            IFDv2ProtocolAction action;
            lock (_protocolLock)
            {
                action = _protocolHandler.HandleEvent(evt);
            }

            switch (action)
            {
                case FDv2ActionChangeset changeAction:
                {
                    var storeError = false;
                    var changeset = changeAction.Changeset;
                    switch (changeset.Type)
                    {
                        case FDv2ChangeSetType.Full:
                        case FDv2ChangeSetType.Partial:
                            storeError = !_transactionalDataSourceUpdates.Apply(
                                FDv2ChangeSetTranslator.ToChangeSet(changeAction.Changeset, _log, _environmentId));
                            break;
                        case FDv2ChangeSetType.None:
                            break;
                        default:
                            _log.Error("Unhandled FDv2 Changeset Type.");
                            break;
                    }

                    if (!storeError)
                    {
                        _lastStoreUpdateFailed.GetAndSet(false);

                        // TODO: This may be more nuanced or not required once we have the composite
                        // data source.
                        MaybeMarkInitialized();
                    }
                    else
                    {
                        HandleStoreError($"failed to write changeset: {changeset.Type}");
                    }
                }

                    break;
                case FDv2ActionError errorAction:
                    _log.Error(errorAction.Reason);
                    break;
                case FDv2ActionGoodbye goodbyeAction:
                    _log.Info(goodbyeAction.Reason);
                    // TODO: Should we handle this proactively in any way?
                    break;
                case FDv2ActionNone _:
                    break;
                case FDv2ActionInternalError internalErrorEvent:
                    _log.Error(internalErrorEvent.Message);
                    switch (internalErrorEvent.ErrorType)
                    {
                        case FDv2ProtocolErrorType.JsonError:
                            HandleJsonError(internalErrorEvent.Message);
                            break;
                        case FDv2ProtocolErrorType.MissingPayload:
                        case FDv2ProtocolErrorType.ProtocolError:
                        case FDv2ProtocolErrorType.UnknownEvent:
                        // TODO: Should we consider restarting in these situations?
                        case FDv2ProtocolErrorType.ImplementationError:
                        default:
                            _log.Error(internalErrorEvent.Message);
                            break;
                    }

                    break;
                default:
                    // Represents an implementation error. Actions expanded without the handling
                    // being expanded.
                    _log.Error("Unhandled FDv2 Protocol Action.");
                    break;
            }

            return;

            void MaybeMarkInitialized()
            {
                if (_initialized.GetAndSet(true)) return;
                _initTask.TrySetResult(true);
                _log.Info("LaunchDarkly streaming is active");
            }
        }

        private void OnError(object sender, ExceptionEventArgs e)
        {
            var ex = e.Exception;
            var recoverable = true;
            DataSourceStatus.ErrorInfo errorInfo;

            if (ex is EventSourceServiceUnsuccessfulResponseException respEx)
            {
                var status = respEx.StatusCode;
                errorInfo = DataSourceStatus.ErrorInfo.FromHttpError(status);
                RecordStreamInit(true);
                if (!HttpErrors.IsRecoverable(status))
                {
                    recoverable = false;
                    _log.Error(HttpErrors.ErrorMessage(status, "streaming connection", ""));
                }
                else
                {
                    _log.Warn(HttpErrors.ErrorMessage(status, "streaming connection", "will retry"));
                }
            }
            else
            {
                errorInfo = DataSourceStatus.ErrorInfo.FromException(ex);
                _log.Warn("Encountered EventSource error: {0}", LogValues.ExceptionSummary(ex));
                _log.Debug(LogValues.ExceptionTrace(ex));
            }

            _dataSourceUpdates.UpdateStatus(recoverable ? DataSourceState.Interrupted : DataSourceState.Off,
                errorInfo);

            if (recoverable) return;
            // Make _initTask complete to tell the client to stop waiting for initialization. We use
            // TrySetResult rather than SetResult here because it might have already been completed
            // (if, for instance, the stream started successfully, then restarted and got a 401).
            _initTask.TrySetResult(false);
            Shutdown();
        }

        private void OnOpen(object sender, StateChangedEventArgs e)
        {
            lock (_protocolLock)
            {
                // Reset the protocol handler whenever the connection opens. We need to discard any partial state
                // that may have accumulated.
                _protocolHandler.Reset();
            }

            _environmentId = e.Headers?.FirstOrDefault((item) =>
                    item.Key.ToLower() == HeaderConstants.EnvironmentId).Value
                ?.FirstOrDefault();
            _log.Debug("EventSource Opened");
            RecordStreamInit(false);
        }

        private void RecordStreamInit(bool failed)
        {
            if (_diagnosticStore == null) return;

            var now = DateTime.Now;
            _diagnosticStore.AddStreamInit(_esStarted, now - _esStarted, failed);
            _esStarted = now;
        }

        private void OnDataStoreStatusChanged(object sender, DataStoreStatus newStatus)
        {
            if (!newStatus.Available || !newStatus.RefreshNeeded) return;

            // The store has just transitioned from unavailable to available, and we can't guarantee that
            // all the latest data got cached, so let's restart the stream to refresh all the data.
            _log.Warn("Restarting stream to refresh data after data store outage");
            _es.Restart(false);
        }

        private IEventSource CreateEventSource(Uri uri, HttpConfiguration httpConfig)
        {
            var configBuilder = EventSource.Configuration.Builder(uri)
                .HttpRequestModifier((req) =>
                {
                    var selector = _selectorSource();
                    if (selector.IsEmpty) return;
                    // Update the request to include the current selector.
                    var queryParams = QueryStringHelper.ParseQueryString(req.RequestUri.Query);
                    queryParams["basis"] = selector.State;
                    var uriBuilder = new UriBuilder(uri)
                    {
                        Query = QueryStringHelper.ToQueryString(queryParams)
                    };
                    req.RequestUri = uriBuilder.Uri;
                })
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
    }
}
