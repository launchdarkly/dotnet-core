using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LaunchDarkly.EventSource;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.Sdk.Server.Interfaces;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    // TODO: Commonize.
    internal delegate FDv2Selector SelectorSource();

    internal delegate IEventSource EventSourceCreator(Uri streamUri,
        HttpConfiguration httpConfig);

    internal sealed class FDv2StreamingDataSource : IDataSource
    {
        // The read timeout for the stream is different from the read timeout that can be set in the SDK configuration.
        // It is a fixed value that is set to be slightly longer than the expected interval between heartbeats
        // from the LaunchDarkly streaming server. If this amount of time elapses with no new data, the connection
        // will be cycled.
        private static readonly TimeSpan LaunchDarklyStreamReadTimeout = TimeSpan.FromMinutes(5);

        private readonly IEventSource _es;
        private readonly TaskCompletionSource<bool> _initTask = new TaskCompletionSource<bool>();
        private IEnumerable<KeyValuePair<string, IEnumerable<string>>> _headers;
        private DateTime _esStarted;
        private readonly AtomicBoolean _initialized = new AtomicBoolean(false);
        private readonly FDv2ProtocolHandler _protocolHandler = new FDv2ProtocolHandler();

        private readonly IDiagnosticStore _diagnosticStore;
        private readonly IDataSourceUpdates _dataSourceUpdates;

        private readonly TimeSpan _initialReconnectDelay;
        private readonly Logger _log;
        private readonly bool _storeStatusMonitoringEnabled;

        private readonly SelectorSource _selectorSource;

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

            // The query parameters will be handles by the event source request
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

        private bool HandleBasis(FDv2ChangeSet changeset,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            // TODO: Check if the data source updates implementation supports the transactional store interface.
            // If it does, then apply the data using that mechanism.
            var putData = FDv2ChangeSetTranslator.TranslatePutData(changeset, _log);
            if (_dataSourceUpdates is IDataSourceUpdatesHeaders dataSourceUpdatesHeaders)
            {
                return dataSourceUpdatesHeaders.InitWithHeaders(putData.Data, headers);
            }

            return _dataSourceUpdates.Init(putData.Data);
        }

        private bool HandlePartial(FDv2ChangeSet changeset)
        {
            // TODO: Check if the data source updates implementation supports the transactional store interface.
            // If it does, then apply the data using that mechanism.
            var patches = FDv2ChangeSetTranslator.TranslatePatchData(changeset, _log);
            var success = true;
            foreach (var patch in patches)
            {
                success = _dataSourceUpdates.Upsert(patch.Kind, patch.Key, patch.Item);
                if (!success)
                {
                    break;
                }
            }

            return success;
        }

        private void OnMessage(object sender, MessageReceivedEventArgs e)
        {
            var data = e.Message.Data != null ? JsonDocument.Parse(e.Message.Data) : null;
            var evt = new FDv2Event(e.Message.Name, data?.RootElement);
            var res = _protocolHandler.HandleEvent(evt);

            switch (res)
            {
                case FDv2ActionChangeset changeAction:
                    var changeset = changeAction.Changeset;
                    switch (changeset.Type)
                    {
                        case FDv2ChangeSetType.Full:
                            HandleBasis(changeset, _headers);
                            // TODO: Handle failed store write.
                            MaybeMarkInitialized();
                            break;
                        case FDv2ChangeSetType.Partial:
                            // TODO: Handle failed store write.
                            HandlePartial(changeset);
                            break;
                        case FDv2ChangeSetType.None:
                            // TODO: Implement.
                            MaybeMarkInitialized();
                            break;
                        default:
                            _log.Error("Unhandled FDv2 Changeset Type.");
                            break;
                    }

                    break;
                case FDv2ActionError errorAction:
                    _log.Error(errorAction.Reason);
                    // TODO: Implement error handling.
                    break;
                case FDv2ActionGoodbye goodbyeAction:
                    _log.Info(goodbyeAction.Reason);
                    // TODO: Don't log an error for the coming disconnect.
                    break;
                case FDv2ActionNone _:
                    break;
                case FDv2ActionInternalError internalErrorEvent:
                    _log.Error(internalErrorEvent.Message);
                    // TODO: Implement error handling.
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
            // TODO: Implement.
        }

        private void OnOpen(object sender, StateChangedEventArgs e)
        {
            _headers = e.Headers;
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
