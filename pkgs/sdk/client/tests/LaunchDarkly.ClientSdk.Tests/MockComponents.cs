using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Client.Internal.DataSources;
using LaunchDarkly.Sdk.Client.Internal.DataStores;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;
using LaunchDarkly.Sdk.Client.PlatformSpecific;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Sdk.Internal.Events;
using LaunchDarkly.TestHelpers;
using Xunit;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client
{
    // Even more so than in the server-side SDK tests, we rely on our own concrete mock
    // implementations of our component interfaces rather than using Moq to create mocks
    // dynamically, because runtime differences cause Moq to fail on some mobile platforms.

    internal static class MockComponentExtensions
    {
        // Normally SDK configuration always specifies component factories rather than component instances,
        // so that the SDK can handle the component lifecycle and dependency injection. However, in tests,
        // we often want to set up a specific component instance; .AsSingletonFactory() wraps it in a
        // factory that always returns that instance.
        public static IComponentConfigurer<T> AsSingletonFactory<T>(this T instance) =>
            MockComponents.ComponentConfigurerFromLambda<T>((clientContext) => instance);

        public static IComponentConfigurer<T> AsSingletonFactoryWithDiagnosticDescription<T>(this T instance, LdValue description) =>
            new SingleComponentFactoryWithDiagnosticDescription<T> { Instance = instance, Description = description };

        private class SingleComponentFactoryWithDiagnosticDescription<T> : IComponentConfigurer<T>, IDiagnosticDescription
        {
            public T Instance { get; set; }
            public LdValue Description { get; set; }
            public LdValue DescribeConfiguration(LdClientContext context) => Description;
            public T Build(LdClientContext context) => Instance;
        }
    }

    internal static class MockComponents
    {
        public static IComponentConfigurer<T> ComponentConfigurerFromLambda<T>(Func<LdClientContext, T> factory) =>
            new ComponentConfigurerFromLambdaImpl<T>() { Factory = factory };

        private class ComponentConfigurerFromLambdaImpl<T> : IComponentConfigurer<T>
        {
            public Func<LdClientContext, T> Factory { get; set; }
            public T Build(LdClientContext context) => Factory(context);
        }
    }

    internal class CapturingComponentConfigurer<T>: IComponentConfigurer<T>
    {
        private readonly IComponentConfigurer<T> _factory;
        public LdClientContext ReceivedClientContext { get; private set; }

        public CapturingComponentConfigurer(IComponentConfigurer<T> factory)
        {
            _factory = factory;
        }

        public T Build(LdClientContext clientContext)
        {
            ReceivedClientContext = clientContext;
            return _factory.Build(clientContext);
        }
    }

    internal class MockBackgroundModeManager : IBackgroundModeManager
    {
        public event EventHandler<BackgroundModeChangedEventArgs> BackgroundModeChanged;

        public void UpdateBackgroundMode(bool isInBackground)
        {
            BackgroundModeChanged?.Invoke(this, new BackgroundModeChangedEventArgs(isInBackground));
        }
    }

    internal class MockConnectivityStateManager : IConnectivityStateManager
    {
        public Action<bool> ConnectionChanged { get; set; }

        public MockConnectivityStateManager(bool isOnline)
        {
            isConnected = isOnline;
        }

        bool isConnected;
        public bool IsConnected
        {
            get
            {
                return isConnected;
            }

            set
            {
                isConnected = value;
            }
        }

        public void Connect(bool online)
        {
            IsConnected = online;
            ConnectionChanged?.Invoke(IsConnected);
        }
    }

    internal class MockDataSourceUpdateSink : IDataSourceUpdateSink
    {
        internal class ReceivedInit
        {
            public FullDataSet Data { get; set; }
            public Context Context{ get; set; }
        }

        internal class ReceivedUpsert
        {
            public string Key { get; set; }
            public ItemDescriptor Data { get; set; }
            public Context Context { get; set; }
        }

        internal class ReceivedStatusUpdate
        {
            public DataSourceState State { get; set; }
            public DataSourceStatus.ErrorInfo? Error { get; set; }
        }

        public readonly EventSink<object> Actions = new EventSink<object>();

        public void Init(Context context, FullDataSet data) =>
            Actions.Enqueue(new ReceivedInit { Data = data, Context = context});

        public void Upsert(Context context, string key, ItemDescriptor data) =>
            Actions.Enqueue(new ReceivedUpsert { Key = key, Data = data, Context = context });

        public void UpdateStatus(DataSourceState newState, DataSourceStatus.ErrorInfo? newError) =>
            Actions.Enqueue(new ReceivedStatusUpdate { State = newState, Error = newError });

        public FullDataSet ExpectInit(Context context)
        {
            var action = Assert.IsType<ReceivedInit>(Actions.ExpectValue(TimeSpan.FromSeconds(5)));
            AssertHelpers.ContextsEqual(context, action.Context);
            return action.Data;
        }

        public ItemDescriptor ExpectUpsert(Context context, string key)
        {
            var action = Assert.IsType<ReceivedUpsert>(Actions.ExpectValue(TimeSpan.FromSeconds(5)));
            AssertHelpers.ContextsEqual(context, action.Context);
            Assert.Equal(key, action.Key);
            return action.Data;
        }

        public DataSourceStatus ExpectStatusUpdate()
        {
            var action = Assert.IsType<ReceivedStatusUpdate>(Actions.ExpectValue(TimeSpan.FromSeconds(5)));
            return new DataSourceStatus { State = action.State, LastError = action.Error };
        }

        public void ExpectNoMoreActions() => Actions.ExpectNoValue();
    }

    internal class MockDiagnosticStore : IDiagnosticStore
    {
        internal struct StreamInit
        {
            internal DateTime Timestamp;
            internal TimeSpan Duration;
            internal bool Failed;
        }

        internal readonly EventSink<StreamInit> StreamInits = new EventSink<StreamInit>();

        public DateTime DataSince => DateTime.Now;

        public DiagnosticEvent? InitEvent => null;

        public DiagnosticEvent? PersistedUnsentEvent => null;

        public void AddStreamInit(DateTime timestamp, TimeSpan duration, bool failed) =>
            StreamInits.Enqueue(new StreamInit { Timestamp = timestamp, Duration = duration, Failed = failed });

        public DiagnosticEvent CreateEventAndReset() => new DiagnosticEvent();

        public void IncrementDeduplicatedUsers() { }

        public void IncrementDroppedEvents() { }

        public void RecordEventsInBatch(long eventsInBatch) { }
    }

    internal class MockEventProcessor : IEventProcessor
    {
        public List<object> Events = new List<object>();
        public bool Offline = false;

        public void SetOffline(bool offline)
        {
            Offline = offline;
        }

        public void Flush() { }

        public bool FlushAndWait(TimeSpan timeout) => true;

        public Task<bool> FlushAndWaitAsync(TimeSpan timeout) => Task.FromResult(true);

        public void Dispose() { }

        public void RecordEvaluationEvent(in EventProcessorTypes.EvaluationEvent e) =>
            Events.Add(e);

        public void RecordIdentifyEvent(in EventProcessorTypes.IdentifyEvent e) =>
            Events.Add(e);

        public void RecordCustomEvent(in EventProcessorTypes.CustomEvent e) =>
            Events.Add(e);
    }

    public class MockEventSender : IEventSender
    {
        public BlockingCollection<Params> Calls = new BlockingCollection<Params>();
        public EventDataKind? FilterKind = null;

        public void Dispose() { }

        public struct Params
        {
            public EventDataKind Kind;
            public string Data;
            public int EventCount;
        }

        public Task<EventSenderResult> SendEventDataAsync(EventDataKind kind, byte[] data, int eventCount)
        {
            if (!FilterKind.HasValue || kind == FilterKind.Value)
            {
                Calls.Add(new Params { Kind = kind, Data = Encoding.UTF8.GetString(data), EventCount = eventCount });
            }
            return Task.FromResult(new EventSenderResult(DeliveryStatus.Succeeded, null));
        }

        public Params RequirePayload()
        {
            Params result;
            if (!Calls.TryTake(out result, TimeSpan.FromSeconds(5)))
            {
                throw new System.Exception("did not receive an event payload");
            }
            return result;
        }

        public void RequireNoPayloadSent(TimeSpan timeout)
        {
            Params result;
            if (Calls.TryTake(out result, timeout))
            {
                throw new System.Exception("received an unexpected event payload");
            }
        }
    }

    internal class MockFeatureFlagRequestor : IFeatureFlagRequestor
    {
        private readonly string _jsonFlags;

        public MockFeatureFlagRequestor(string jsonFlags)
        {
            _jsonFlags = jsonFlags;
        }

        public void Dispose()
        {

        }

        public Task<WebResponse> FeatureFlagsAsync()
        {
            var response = new WebResponse(200, _jsonFlags, null);
            return Task.FromResult(response);
        }
    }

    internal class MockPersistentDataStore : IPersistentDataStore
    {
        private Dictionary<(string, string), string> _map = new Dictionary<(string, string), string>();

        public void Dispose() { }

        public string GetValue(string storageNamespace, string key) =>
            _map.TryGetValue((storageNamespace, key), out var value) ? value : null;

        public void SetValue(string storageNamespace, string key, string value)
        {
            if (value is null)
            {
                _map.Remove((storageNamespace, key));
            }
            else
            {
                _map[(storageNamespace, key)] = value;
            }
        }

        public ImmutableList<string> GetKeys(string storageNamespace) =>
            _map.Where(kv => kv.Key.Item1 == storageNamespace).Select(kv => kv.Value).ToImmutableList();

        private PersistentDataStoreWrapper WithWrapper(string mobileKey) =>
            new PersistentDataStoreWrapper(this, mobileKey, Logs.None.Logger(""));

        internal void SetupUserData(string mobileKey, string contextKey, FullDataSet data) =>
            WithWrapper(mobileKey).SetContextData(Base64.UrlSafeSha256Hash(contextKey), data);

        internal FullDataSet? InspectUserData(string mobileKey, string contextKey) =>
            WithWrapper(mobileKey).GetContextData(Base64.UrlSafeSha256Hash(contextKey));

        internal ContextIndex InspectContextIndex(string mobileKey) =>
            WithWrapper(mobileKey).GetIndex();
    }

    internal class MockPollingProcessor : IDataSource
    {
        private IDataSourceUpdateSink _updateSink;
        private Context _context;
        private FullDataSet? _data;

        public MockPollingProcessor(FullDataSet? data) : this(null, new Context(), data) { }

        private MockPollingProcessor(IDataSourceUpdateSink updateSink, Context context, FullDataSet? data)
        {
            _updateSink = updateSink;
            _context = context;
            _data = data;
        }

        public static IComponentConfigurer<IDataSource> Factory(FullDataSet? data) =>
            MockComponents.ComponentConfigurerFromLambda<IDataSource>(clientContext =>
                new MockPollingProcessor(clientContext.DataSourceUpdateSink, clientContext.CurrentContext, data));

        public bool IsRunning
        {
            get;
            set;
        }

        public void Dispose()
        {
            IsRunning = false;
        }

        public bool Initialized => IsRunning;

        public Task<bool> Start()
        {
            IsRunning = true;
            if (_updateSink != null && _data != null)
            {
                _updateSink.Init(_context, _data.Value);
            }
            return Task.FromResult(true);
        }
    }

    internal class MockDataSourceFromLambda : IDataSource
    {
        private readonly Context _context;
        private readonly Func<Task> _startFn;
        private bool _initialized;

        public MockDataSourceFromLambda(Context context, Func<Task> startFn)
        {
            _context = context;
            _startFn = startFn;
        }

        public Task<bool> Start()
        {
            return _startFn().ContinueWith<bool>(t =>
            {
                _initialized = true;
                return true;
            });
        }

        public bool Initialized => _initialized;

        public void Dispose() { }
    }

    internal class MockDataSource : IDataSource
    {
        public bool IsRunning => true;

        public void Dispose() { }

        public bool Initialized => true;

        public Task<bool> Start() => Task.FromResult(true);
    }

    internal class MockDataSourceThatNeverInitializes : IDataSource
    {
        public bool IsRunning => false;

        public void Dispose() { }

        public bool Initialized => false;

        public Task<bool> Start() => new TaskCompletionSource<bool>().Task; // will never be completed
    }
}
