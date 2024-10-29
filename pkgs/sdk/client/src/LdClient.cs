using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal;
using LaunchDarkly.Sdk.Client.Internal.DataSources;
using LaunchDarkly.Sdk.Client.Internal.DataStores;
using LaunchDarkly.Sdk.Client.Internal.Events;
using LaunchDarkly.Sdk.Client.Internal.Interfaces;
using LaunchDarkly.Sdk.Client.PlatformSpecific;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Concurrent;

namespace LaunchDarkly.Sdk.Client
{
    /// <summary>
    /// A client for the LaunchDarkly API. Client instances are thread-safe. Your application should instantiate
    /// a single <c>LdClient</c> for the lifetime of their application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like all client-side LaunchDarkly SDKs, the <c>LdClient</c> always has a single current <see cref="Context"/>.
    /// You specify this context at initialization time, and you can change it later with <see cref="Identify(Sdk.Context,TimeSpan)"/>
    /// or <see cref="IdentifyAsync(Sdk.Context)"/>. All subsequent calls to evaluation methods like
    /// <see cref="BoolVariation(string, bool)"/> refer to the flag values for the current context.
    /// </para>
    /// <para>
    /// Normally, the SDK uses the exact context that you have specified in the <see cref="Context"/>. However,
    /// you can also tell the SDK to generate a randomized identifier and use this as the context's
    /// <see cref="Context.Key"/>; see <see cref="ConfigurationBuilder.GenerateAnonymousKeys(bool)"/>.
    /// </para>
    /// <para>
    /// If you use more than one <see cref="ContextKind"/> in your evaluation contexts, and you request a
    /// randomized key as described above, a different key is generated for each kind.
    /// </para>
    /// </remarks>
    public sealed class LdClient : ILdClient
    {
        static readonly EventFactory _eventFactoryDefault = EventFactory.Default;
        static readonly EventFactory _eventFactoryWithReasons = EventFactory.DefaultWithReasons;

        static readonly object _createInstanceLock = new object();
        static volatile LdClient _instance;

        private readonly TimeSpan ExcessiveInitWaitTime = TimeSpan.FromSeconds(15);

        private const String ExcessiveInitWaitTimeWarning =
            "LDClient.Init called with max wait time parameter of {0} seconds.  We recommend a timeout of less than {1} seconds.";

        private const String DidNotInitializeTimelyWarning = "Client did not initialize within {0} milliseconds.";

        // Immutable client state
        readonly Configuration _config;
        readonly LdClientContext _clientContext;
        readonly IDataSourceStatusProvider _dataSourceStatusProvider;
        readonly IDataSourceUpdateSink _dataSourceUpdateSink;
        readonly FlagDataManager _dataStore;
        readonly ConnectionManager _connectionManager;
        readonly IBackgroundModeManager _backgroundModeManager;
        readonly IConnectivityStateManager _connectivityStateManager;
        readonly IEventProcessor _eventProcessor;
        readonly IFlagTracker _flagTracker;
        readonly TaskExecutor _taskExecutor;
        readonly AnonymousKeyContextDecorator _anonymousKeyContextDecorator;
        private readonly AutoEnvContextDecorator _autoEnvContextDecorator;

        private readonly Logger _log;

        // Mutable client state (some state is also in the ConnectionManager)
        readonly ReaderWriterLockSlim _stateLock = new ReaderWriterLockSlim();
        private Context _context;

        /// <summary>
        /// The singleton instance used by your application throughout its lifetime. Once this exists, you cannot
        /// create a new client instance unless you first call <see cref="Dispose()"/> on this one.
        /// </summary>
        /// <remarks>
        /// Use the static factory methods <see cref="Init(Configuration, Sdk.Context, TimeSpan)"/> or
        /// <see cref="InitAsync(Configuration, Sdk.Context)"/> to set this <see cref="LdClient"/> instance.
        /// </remarks>
        public static LdClient Instance => _instance;

        /// <summary>
        /// The current version string of the SDK.
        /// </summary>
        public static Version Version => AssemblyVersions.GetAssemblyVersionForType(typeof(LdClient));

        /// <summary>
        /// The <see cref="Configuration"/> instance used to set up the LdClient.
        /// </summary>
        public Configuration Config => _config;

        /// <summary>
        /// The current evaluation context for all SDK operations.
        /// </summary>
        /// <remarks>
        /// This is initially the context specified for <see cref="Init(Configuration, Sdk.Context, TimeSpan)"/> or
        /// <see cref="InitAsync(Configuration, Sdk.Context)"/>, but can be changed later with
        /// <see cref="Identify(Sdk.Context,TimeSpan)"/> or <see cref="IdentifyAsync(Sdk.Context)"/>.
        /// </remarks>
        public Context Context => LockUtils.WithReadLock(_stateLock, () => _context);

        /// <inheritdoc/>
        public bool Offline => _connectionManager.ForceOffline;

        /// <inheritdoc/>
        public bool Initialized => _connectionManager.Initialized;

        /// <inheritdoc/>
        public IDataSourceStatusProvider DataSourceStatusProvider => _dataSourceStatusProvider;

        /// <inheritdoc/>
        public IFlagTracker FlagTracker => _flagTracker;

        // private constructor prevents initialization of this class
        // without using WithConfigAnduser(config, user)
        LdClient()
        {
        }

        LdClient(Configuration configuration, Context initialContext, TimeSpan startWaitTime)
        {
            _config = configuration ?? throw new ArgumentNullException(nameof(configuration));
            var baseContext = new LdClientContext(_config, initialContext, this);

            var diagnosticStore = _config.DiagnosticOptOut
                ? null
                : new ClientDiagnosticStore(baseContext, _config, startWaitTime);
            var diagnosticDisabler = _config.DiagnosticOptOut ? null : new DiagnosticDisablerImpl();
            _clientContext = baseContext.WithDiagnostics(diagnosticDisabler, diagnosticStore);

            _log = _clientContext.BaseLogger;
            _taskExecutor = _clientContext.TaskExecutor;

            _log.Info("Starting LaunchDarkly Client {0} built with target framework {1}", Version,
                SdkPackage.DotNetTargetFramework);

            var persistenceConfiguration = (_config.PersistenceConfigurationBuilder ?? Components.Persistence())
                .Build(_clientContext);
            _dataStore = new FlagDataManager(
                _config.MobileKey,
                persistenceConfiguration,
                _log.SubLogger(LogNames.DataStoreSubLog)
            );

            _anonymousKeyContextDecorator =
                new AnonymousKeyContextDecorator(_dataStore.PersistentStore, _config.GenerateAnonymousKeys);
            var decoratedContext = _anonymousKeyContextDecorator.DecorateContext(initialContext);

            if (_config.AutoEnvAttributes)
            {
                _autoEnvContextDecorator = new AutoEnvContextDecorator(_dataStore.PersistentStore,
                    _clientContext.EnvironmentReporter, _log);
                decoratedContext = _autoEnvContextDecorator.DecorateContext(decoratedContext);
            }

            _context = decoratedContext;

            // If we had cached data for the new context, set the current in-memory flag data state to use
            // that data, so that any Variation calls made before Identify has completed will use the
            // last known values.
            var cachedData = _dataStore.GetCachedData(_context);
            if (cachedData != null)
            {
                _log.Debug("Cached flag data is available for this context");
                _dataStore.Init(_context, cachedData.Value,
                    false); // false means "don't rewrite the flags to persistent storage"
            }

            var dataSourceUpdateSink = new DataSourceUpdateSinkImpl(
                _dataStore,
                _config.Offline,
                _taskExecutor,
                _log.SubLogger(LogNames.DataSourceSubLog)
            );
            _dataSourceUpdateSink = dataSourceUpdateSink;

            _dataSourceStatusProvider = new DataSourceStatusProviderImpl(dataSourceUpdateSink);
            _flagTracker = new FlagTrackerImpl(dataSourceUpdateSink);

            var dataSourceFactory = _config.DataSource ?? Components.StreamingDataSource();

            _connectivityStateManager = Factory.CreateConnectivityStateManager(_config);
            var isConnected = _connectivityStateManager.IsConnected;

            diagnosticDisabler?.SetDisabled(!isConnected || _config.Offline);

            _eventProcessor = (_config.Events ?? Components.SendEvents())
                .Build(_clientContext);
            _eventProcessor.SetOffline(_config.Offline || !isConnected);

            _connectionManager = new ConnectionManager(
                _clientContext,
                dataSourceFactory,
                _dataSourceUpdateSink,
                _eventProcessor,
                diagnosticDisabler,
                _config.EnableBackgroundUpdating,
                _context,
                _log
            );
            _connectionManager.SetForceOffline(_config.Offline);
            _connectionManager.SetNetworkEnabled(isConnected);
            if (_config.Offline)
            {
                _log.Info("Starting LaunchDarkly client in offline mode");
            }

            _connectivityStateManager.ConnectionChanged += networkAvailable =>
            {
                _log.Debug("Setting online to {0} due to a connectivity change event", networkAvailable);
                _ = _connectionManager.SetNetworkEnabled(networkAvailable); // do not await the result
            };

            // Send an initial identify event, but only if we weren't explicitly set to be offline

            if (!_config.Offline)
            {
                _eventProcessor.RecordIdentifyEvent(new EventProcessorTypes.IdentifyEvent
                {
                    Timestamp = UnixMillisecondTime.Now,
                    Context = _context
                });
            }

            _backgroundModeManager = _config.BackgroundModeManager ?? new DefaultBackgroundModeManager();
            _backgroundModeManager.BackgroundModeChanged += OnBackgroundModeChanged;
        }

        void Start(TimeSpan maxWaitTime)
        {
            if (maxWaitTime >= ExcessiveInitWaitTime)
            {
                _log.Warn(ExcessiveInitWaitTimeWarning, maxWaitTime, ExcessiveInitWaitTime);
            }

            var success = AsyncUtils.WaitSafely(() => _connectionManager.Start(), maxWaitTime);
            if (!success)
            {
                _log.Warn(DidNotInitializeTimelyWarning, maxWaitTime.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Starts the client and waits up to the wait time to initialize feature flags.  If offline,
        /// returns immediately.
        /// </summary>
        /// <param name="maxWaitTime">the maximum length of time to wait for the client to initialize</param>
        async Task StartAsync(TimeSpan  maxWaitTime)
        {
            if (maxWaitTime >= ExcessiveInitWaitTime)
            {
                _log.Warn(
                    ExcessiveInitWaitTimeWarning,
                    maxWaitTime, ExcessiveInitWaitTime);
            }

            var startTask = _connectionManager.Start();
            var completedTask = await Task.WhenAny(startTask, Task.Delay(maxWaitTime));
            if (completedTask != startTask)
            {
                _log.Warn(DidNotInitializeTimelyWarning, maxWaitTime.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Starts the client and waits to initialize feature flags.  If offline, returns immediately.
        /// </summary>
        async Task StartAsync()
        {
            await _connectionManager.Start();
        }

        /// <summary>
        /// Creates a new <see cref="LdClient"/> singleton instance and attempts to initialize feature flags.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The constructor will return the <see cref="LdClient"/> instance once the first response from
        /// the LaunchDarkly service is returned, or immediately if offline, or when the the specified
        /// wait time elapses.  If the max wait time elapses, the returned instance will have
        /// an <see cref="Initialized"/> property of <see langword="false"/>, but the instance will continue
        /// trying to get fresh feature flags.
        /// </para>
        /// <para>
        /// To specify additional configuration options rather than just the mobile key, use
        /// <see cref="Init(Configuration, Sdk.Context, TimeSpan)"/>.
        /// </para>
        /// <para>
        /// If you would rather an asynchronous version of this method, use
        /// <see cref="InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context, TimeSpan)"/>.
        /// </para>
        /// <para>
        /// You must use one of these static factory methods to instantiate the single instance of LdClient
        /// for the lifetime of your application.
        /// </para>
        /// </remarks>
        /// <param name="mobileKey">the mobile key given to you by LaunchDarkly</param>
        /// <param name="autoEnvAttributes">Enable / disable Auto Environment Attributes functionality.  When enabled,
        /// the SDK will automatically provide data about the environment where the application is running.
        /// This data makes it simpler to target your mobile customers based on application name or version, or on
        /// device characteristics including manufacturer, model, operating system, locale, and so on. We recommend
        /// enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a> for
        /// more details.</param>
        /// <param name="initialContext">the initial evaluation context; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <param name="maxWaitTime">the maximum length of time to wait for the client to initialize</param>
        /// <returns>the singleton <see cref="LdClient"/> instance</returns>
        /// <seealso cref="Init(Configuration, Sdk.Context, TimeSpan)"/>
        /// <seealso cref="Init(string, ConfigurationBuilder.AutoEnvAttributes, User, TimeSpan)"/>
        /// <seealso cref="InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context)"/>
        public static LdClient Init(string mobileKey, ConfigurationBuilder.AutoEnvAttributes autoEnvAttributes,
            Context initialContext, TimeSpan maxWaitTime)
        {
            var config = Configuration.Default(mobileKey, autoEnvAttributes);

            return Init(config, initialContext, maxWaitTime);
        }

        /// <summary>
        /// Creates a new <see cref="LdClient"/> singleton instance and attempts to initialize feature flags.
        /// </summary>
        /// <remarks>
        /// This is equivalent to
        /// <see cref="Init(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context, TimeSpan)"/>, but using the
        /// <see cref="User"/> type instead of <see cref="Context"/>.
        /// </remarks>
        /// <param name="mobileKey">the mobile key given to you by LaunchDarkly</param>
        /// <param name="autoEnvAttributes">Enable / disable Auto Environment Attributes functionality.  When enabled,
        /// the SDK will automatically provide data about the environment where the application is running.
        /// This data makes it simpler to target your mobile customers based on application name or version, or on
        /// device characteristics including manufacturer, model, operating system, locale, and so on. We recommend
        /// enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a> for
        /// more details.</param>
        /// <param name="initialUser">the initial user attributes; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <param name="maxWaitTime">the maximum length of time to wait for the client to initialize</param>
        /// <returns>the singleton <see cref="LdClient"/> instance</returns>
        /// <seealso cref="Init(Configuration, User, TimeSpan)"/>
        /// <seealso cref="Init(string, ConfigurationBuilder.AutoEnvAttributes, User, TimeSpan)"/>
        /// <seealso cref="InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context)"/>
        [Obsolete("User has been superseded by Context, use Init(string, ConfigurationBuilder.AutoEnvAttributes, Context, TimeSpan) instead.")]
        public static LdClient Init(string mobileKey, ConfigurationBuilder.AutoEnvAttributes autoEnvAttributes,
            User initialUser, TimeSpan maxWaitTime) =>
            Init(mobileKey, autoEnvAttributes, Context.FromUser(initialUser), maxWaitTime);

        /// <summary>
        /// Creates a new <see cref="LdClient"/> singleton instance and attempts to initialize feature flags
        /// asynchronously.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The returned task will yield the <see cref="LdClient"/> instance once the first response from
        /// the LaunchDarkly service is returned or immediately if it is offline.
        /// </para>
        /// <para>
        /// To specify additional configuration options rather than just the mobile key, you can use
        /// <see cref="Init(Configuration, Sdk.Context, TimeSpan)"/> or <see cref="InitAsync(Configuration, Sdk.Context)"/>.
        /// </para>
        /// <para>
        /// If you would rather a synchronous version of this method, use
        /// <see cref="Init(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context, TimeSpan)"/>.
        /// </para>
        /// <para>
        /// You must use one of these static factory methods to instantiate the single instance of LdClient
        /// for the lifetime of your application.
        /// </para>
        /// </remarks>
        /// <param name="mobileKey">the mobile key given to you by LaunchDarkly</param>
        /// <param name="autoEnvAttributes">Enable / disable Auto Environment Attributes functionality.  When enabled,
        /// the SDK will automatically provide data about the environment where the application is running.
        /// This data makes it simpler to target your mobile customers based on application name or version, or on
        /// device characteristics including manufacturer, model, operating system, locale, and so on. We recommend
        /// enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a> for
        /// more details.</param>
        /// <param name="initialContext">the initial evaluation context; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <returns>a Task that resolves to the singleton LdClient instance</returns>
        [Obsolete("Initializing the LDClient without a timeout is no longer permitted to help prevent " +
                  "consumers from blocking their application execution by mistake when connectivity is poor.  Please " +
                  "use InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, Context, TimeSpan) and specify a max wait time.")]
        public static async Task<LdClient> InitAsync(string mobileKey,
            ConfigurationBuilder.AutoEnvAttributes autoEnvAttributes, Context initialContext)
        {
            var config = Configuration.Default(mobileKey, autoEnvAttributes);
            return await InitAsync(config, initialContext);
        }

        /// <summary>
        /// Creates a new <see cref="LdClient"/> singleton instance and attempts to initialize feature flags
        /// asynchronously.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The returned task will yield the <see cref="LdClient"/> instance once the first response from
        /// the LaunchDarkly service is returned, or immediately if offline, or when the the specified
        /// wait time elapses.  If the max wait time elapses, the returned instance will have
        /// an <see cref="Initialized"/> property of <see langword="false"/>, and the instance will continue
        /// trying to get fresh feature flags.
        /// </para>
        /// <para>
        /// If you would rather this happen synchronously, use
        /// <see cref="Init(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context, TimeSpan)"/>. To
        /// specify additional configuration options rather than just the mobile key, you can use
        /// <see cref="Init(Configuration, Sdk.Context, TimeSpan)"/> or <see cref="InitAsync(Configuration, Sdk.Context)"/>.
        /// </para>
        /// <para>
        /// You must use one of these static factory methods to instantiate the single instance of LdClient
        /// for the lifetime of your application.
        /// </para>
        /// </remarks>
        /// <param name="mobileKey">the mobile key given to you by LaunchDarkly</param>
        /// <param name="autoEnvAttributes">Enable / disable Auto Environment Attributes functionality.  When enabled,
        /// the SDK will automatically provide data about the environment where the application is running.
        /// This data makes it simpler to target your mobile customers based on application name or version, or on
        /// device characteristics including manufacturer, model, operating system, locale, and so on. We recommend
        /// enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a> for
        /// more details.</param>
        /// <param name="initialContext">the initial evaluation context; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <param name="maxWaitTime">the maximum length of time to wait for the client to initialize</param>
        /// <returns>a Task that resolves to the singleton LdClient instance</returns>
        public static async Task<LdClient> InitAsync(string mobileKey,
            ConfigurationBuilder.AutoEnvAttributes autoEnvAttributes, Context initialContext, TimeSpan maxWaitTime)
        {
            var config = Configuration.Default(mobileKey, autoEnvAttributes);
            return await InitAsync(config, initialContext, maxWaitTime);
        }

        /// <summary>
        /// Creates a new <see cref="LdClient"/> singleton instance and attempts to initialize feature flags
        /// asynchronously.
        /// </summary>
        /// <remarks>
        /// This is equivalent to
        /// <see cref="InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context)"/>, but using the
        /// <see cref="User"/> type instead of <see cref="Context"/>.
        /// </remarks>
        /// <param name="mobileKey">the mobile key given to you by LaunchDarkly</param>
        /// <param name="autoEnvAttributes">Enable / disable Auto Environment Attributes functionality.  When enabled,
        /// the SDK will automatically provide data about the environment where the application is running.
        /// This data makes it simpler to target your mobile customers based on application name or version, or on
        /// device characteristics including manufacturer, model, operating system, locale, and so on. We recommend
        /// enabling this when you configure the SDK.  See
        /// <a href="https://docs.launchdarkly.com/sdk/features/environment-attributes">our documentation</a> for
        /// more details.</param>
        /// <param name="initialUser">the initial user attributes</param>
        /// <returns>a Task that resolves to the singleton LdClient instance</returns>
        [Obsolete("User has been superseded by Context, use Init(string, ConfigurationBuilder.AutoEnvAttributes, Context) instead.")]
        public static Task<LdClient> InitAsync(string mobileKey,
            ConfigurationBuilder.AutoEnvAttributes autoEnvAttributes, User initialUser) =>
            InitAsync(mobileKey, autoEnvAttributes, Context.FromUser(initialUser));

        /// <summary>
        /// Creates and returns a new LdClient singleton instance, then starts the workflow for
        /// fetching Feature Flags.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The constructor will return the <see cref="LdClient"/> instance once the first response from
        /// the LaunchDarkly service is returned, or immediately if offline, or when the the specified
        /// wait time elapses.  If the max wait time elapses, the returned instance will have
        /// an <see cref="Initialized"/> property of <see langword="false"/>, but the instance will continue
        /// trying to get fresh feature flags.
        /// </para>
        /// <para>
        /// If you do not need to specify configuration options other than the mobile key, you can use
        /// <see cref="Init(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context, TimeSpan)"/>.
        /// </para>
        /// <para>
        /// If you would rather an asynchronous version of this method, use
        /// <see cref="InitAsync(Configuration, Sdk.Context, TimeSpan)"/>.
        /// </para>
        /// <para>
        /// You must use one of these static factory methods to instantiate the single instance of LdClient
        /// for the lifetime of your application.
        /// </para>
        /// </remarks>
        /// <param name="config">the client configuration</param>
        /// <param name="initialContext">the initial evaluation context; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <param name="maxWaitTime">the maximum length of time to wait for the client to initialize;
        /// if this time elapses, the method will not throw an exception but will return the client in
        /// an uninitialized state</param>
        /// <returns>the singleton LdClient instance</returns>
        /// <seealso cref="Init(Configuration, User, TimeSpan)"/>
        /// <seealso cref="Init(string, ConfigurationBuilder.AutoEnvAttributes, User, TimeSpan)"/>
        /// <seealso cref="InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context)"/>
        public static LdClient Init(Configuration config, Context initialContext, TimeSpan maxWaitTime)
        {
            if (maxWaitTime.Ticks < 0 && maxWaitTime != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWaitTime));
            }

            var c = CreateInstance(config, initialContext, maxWaitTime);
            c.Start(maxWaitTime);
            return c;
        }

        /// <summary>
        /// Creates and returns a new LdClient singleton instance, then starts the workflow for
        /// fetching Feature Flags.
        /// </summary>
        /// <remarks>
        /// This is equivalent to <see cref="Init(Configuration, Sdk.Context, TimeSpan)"/>, but using the
        /// <see cref="User"/> type instead of <see cref="Context"/>.
        /// </remarks>
        /// <param name="config">the client configuration</param>
        /// <param name="initialUser">the initial user attributes</param>
        /// <param name="maxWaitTime">the maximum length of time to wait for the client to initialize;
        /// if this time elapses, the method will not throw an exception but will return the client in
        /// an uninitialized state</param>
        /// <returns>the singleton LdClient instance</returns>
        /// <seealso cref="Init(Configuration, Sdk.Context, TimeSpan)"/>
        /// <seealso cref="Init(string, ConfigurationBuilder.AutoEnvAttributes, User, TimeSpan)"/>
        /// <seealso cref="InitAsync(Configuration, User)"/>
        ///
        [Obsolete("User has been superseded by Context, use Init(Configuration, Context, TimeSpan) instead.")]
        public static LdClient Init(Configuration config, User initialUser, TimeSpan maxWaitTime) =>
            Init(config, Context.FromUser(initialUser), maxWaitTime);

        /// <summary>
        /// Creates a new <see cref="LdClient"/> singleton instance and attempts to initialize feature flags
        /// asynchronously.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The returned task will yield the <see cref="LdClient"/> instance once the first response from
        /// the LaunchDarkly service is returned (or immediately if it is in offline mode).
        /// </para>
        /// <para>
        /// If you would rather this happen synchronously, use <see cref="Init(Configuration, Sdk.Context, TimeSpan)"/>.
        /// If you do not need to specify configuration options other than the mobile key, you can use
        /// <see cref="InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context)"/>.
        /// </para>
        /// <para>
        /// You must use one of these static factory methods to instantiate the single instance of LdClient
        /// for the lifetime of your application.
        /// </para>
        /// </remarks>
        /// <param name="config">the client configuration</param>
        /// <param name="initialContext">the initial evaluation context; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <returns>a Task that resolves to the singleton LdClient instance</returns>
        /// <seealso cref="InitAsync(Configuration, User)"/>
        /// <seealso cref="InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, Sdk.Context)"/>
        /// <seealso cref="Init(Configuration, Sdk.Context, TimeSpan)"/>
        [Obsolete("Initializing the LDClient without a timeout is no longer permitted to help prevent " +
                  "consumers from blocking their application execution by mistake when connectivity is poor.  Please " +
                  "use InitAsync(Configuration, Context, TimeSpan) and specify a max wait time.")]
        public static async Task<LdClient> InitAsync(Configuration config, Context initialContext)
        {
            var c = CreateInstance(config, initialContext, TimeSpan.Zero);
            await c.StartAsync();
            return c;
        }

        /// <summary>
        /// Creates a new <see cref="LdClient"/> singleton instance and attempts to initialize feature flags
        /// asynchronously.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The returned task will yield the <see cref="LdClient"/> instance once the first response from
        /// the LaunchDarkly service is returned, or immediately if offline, or when the the specified
        /// wait time elapses.  If the max wait time elapses, the returned instance will have
        /// an <see cref="Initialized"/> property of <see langword="false"/>, and the instance will continue
        /// trying to get fresh feature flags.
        /// </para>
        /// <para>
        /// If you would rather this happen synchronously, use <see cref="Init(Configuration, Sdk.Context, TimeSpan)"/>
        /// </para>
        /// <para>
        /// You must use one of these static factory methods to instantiate the single instance of LdClient
        /// for the lifetime of your application.
        /// </para>
        /// </remarks>
        /// <param name="config">the client configuration</param>
        /// <param name="initialContext">the initial evaluation context; see <see cref="LdClient"/> for more
        /// about setting the context and optionally requesting a unique key for it</param>
        /// <param name="maxWaitTime">the maximum length of time to wait for the client to initialize</param>
        /// <returns>a Task that resolves to the singleton LdClient instance</returns>
        public static async Task<LdClient> InitAsync(Configuration config, Context initialContext, TimeSpan maxWaitTime)
        {
            var c = CreateInstance(config, initialContext, TimeSpan.Zero);
            await c.StartAsync(maxWaitTime);
            return c;
        }

        /// <summary>
        /// Creates a new <see cref="LdClient"/> singleton instance and attempts to initialize feature flags
        /// asynchronously.
        /// </summary>
        /// <remarks>
        /// This is equivalent to <see cref="InitAsync(Configuration, Sdk.Context)"/>, but using the
        /// <see cref="User"/> type instead of <see cref="Context"/>.
        /// </remarks>
        /// <param name="config">the client configuration</param>
        /// <param name="initialUser">the initial user attributes</param>
        /// <returns>a Task that resolves to the singleton LdClient instance</returns>
        /// <seealso cref="InitAsync(Configuration, Sdk.Context)"/>
        /// <seealso cref="InitAsync(string, ConfigurationBuilder.AutoEnvAttributes, User)"/>
        /// <seealso cref="Init(Configuration, User, TimeSpan)"/>
        [Obsolete("User has been superseded by Context, use InitAsync(Configuration, Context) instead.")]
        public static Task<LdClient> InitAsync(Configuration config, User initialUser) =>
            InitAsync(config, Context.FromUser(initialUser));

        static LdClient CreateInstance(Configuration configuration, Context initialContext, TimeSpan maxWaitTime)
        {
            lock (_createInstanceLock)
            {
                if (_instance != null)
                {
                    throw new Exception("LdClient instance already exists.");
                }

                var c = new LdClient(configuration, initialContext, maxWaitTime);
                _instance = c;
                return c;
            }
        }

        /// <inheritdoc/>
        public bool SetOffline(bool value, TimeSpan maxWaitTime)
        {
            return AsyncUtils.WaitSafely(() => SetOfflineAsync(value), maxWaitTime);
        }

        /// <inheritdoc/>
        public async Task SetOfflineAsync(bool value)
        {
            _eventProcessor.SetOffline(value || !_connectionManager.NetworkEnabled);
            await _connectionManager.SetForceOffline(value);
        }

        /// <inheritdoc/>
        public bool BoolVariation(string key, bool defaultValue = false)
        {
            return VariationInternal<bool>(key, LdValue.Of(defaultValue), LdValue.Convert.Bool, true,
                _eventFactoryDefault).Value;
        }

        /// <inheritdoc/>
        public EvaluationDetail<bool> BoolVariationDetail(string key, bool defaultValue = false)
        {
            return VariationInternal<bool>(key, LdValue.Of(defaultValue), LdValue.Convert.Bool, true,
                _eventFactoryWithReasons);
        }

        /// <inheritdoc/>
        public string StringVariation(string key, string defaultValue)
        {
            return VariationInternal<string>(key, LdValue.Of(defaultValue), LdValue.Convert.String, true,
                _eventFactoryDefault).Value;
        }

        /// <inheritdoc/>
        public EvaluationDetail<string> StringVariationDetail(string key, string defaultValue)
        {
            return VariationInternal<string>(key, LdValue.Of(defaultValue), LdValue.Convert.String, true,
                _eventFactoryWithReasons);
        }

        /// <inheritdoc/>
        public float FloatVariation(string key, float defaultValue = 0)
        {
            return VariationInternal<float>(key, LdValue.Of(defaultValue), LdValue.Convert.Float, true,
                _eventFactoryDefault).Value;
        }

        /// <inheritdoc/>
        public EvaluationDetail<float> FloatVariationDetail(string key, float defaultValue = 0)
        {
            return VariationInternal<float>(key, LdValue.Of(defaultValue), LdValue.Convert.Float, true,
                _eventFactoryWithReasons);
        }

        /// <inheritdoc/>
        public double DoubleVariation(string key, double defaultValue = 0)
        {
            return VariationInternal<double>(key, LdValue.Of(defaultValue), LdValue.Convert.Double, true,
                _eventFactoryDefault).Value;
        }

        /// <inheritdoc/>
        public EvaluationDetail<double> DoubleVariationDetail(string key, double defaultValue = 0)
        {
            return VariationInternal<double>(key, LdValue.Of(defaultValue), LdValue.Convert.Double, true,
                _eventFactoryWithReasons);
        }

        /// <inheritdoc/>
        public int IntVariation(string key, int defaultValue = 0)
        {
            return VariationInternal(key, LdValue.Of(defaultValue), LdValue.Convert.Int, true, _eventFactoryDefault)
                .Value;
        }

        /// <inheritdoc/>
        public EvaluationDetail<int> IntVariationDetail(string key, int defaultValue = 0)
        {
            return VariationInternal(key, LdValue.Of(defaultValue), LdValue.Convert.Int, true,
                _eventFactoryWithReasons);
        }

        /// <inheritdoc/>
        public LdValue JsonVariation(string key, LdValue defaultValue)
        {
            return VariationInternal(key, defaultValue, LdValue.Convert.Json, false, _eventFactoryDefault).Value;
        }

        /// <inheritdoc/>
        public EvaluationDetail<LdValue> JsonVariationDetail(string key, LdValue defaultValue)
        {
            return VariationInternal(key, defaultValue, LdValue.Convert.Json, false, _eventFactoryWithReasons);
        }

        EvaluationDetail<T> VariationInternal<T>(string featureKey, LdValue defaultJson, LdValue.Converter<T> converter,
            bool checkType, EventFactory eventFactory)
        {
            T defaultValue = converter.ToType(defaultJson);

            EvaluationDetail<T> errorResult(EvaluationErrorKind kind) =>
                new EvaluationDetail<T>(defaultValue, null, EvaluationReason.ErrorReason(kind));

            var flag = _dataStore.Get(featureKey)?.Item;
            if (flag == null)
            {
                if (!Initialized)
                {
                    _log.Warn("LaunchDarkly client has not yet been initialized. Returning default value");
                    SendEvaluationEventIfOnline(eventFactory.NewUnknownFlagEvaluationEvent(featureKey, Context,
                        defaultJson,
                        EvaluationErrorKind.ClientNotReady));
                    return errorResult(EvaluationErrorKind.ClientNotReady);
                }
                else
                {
                    _log.Info("Unknown feature flag {0}; returning default value", featureKey);
                    SendEvaluationEventIfOnline(eventFactory.NewUnknownFlagEvaluationEvent(featureKey, Context,
                        defaultJson,
                        EvaluationErrorKind.FlagNotFound));
                    return errorResult(EvaluationErrorKind.FlagNotFound);
                }
            }
            else
            {
                if (!Initialized)
                {
                    _log.Warn("LaunchDarkly client has not yet been initialized. Returning cached value");
                }
            }

            // The flag.Prerequisites array represents the evaluated prerequisites of this flag. We need to generate
            // events for both this flag and its prerequisites (recursively), which is necessary to ensure LaunchDarkly
            // analytics functions properly.
            //
            // We're using JsonVariationDetail because the type of the prerequisite is both unknown and irrelevant
            // to emitting the events.
            //
            // We're passing LdValue.Null to match a server-side SDK's behavior when evaluating prerequisites.
            //
            // NOTE: if "hooks" functionality is implemented into this SDK, take care that evaluating prerequisites
            // does not trigger hooks. This may require refactoring the code below to not use JsonVariationDetail.
            if (flag.Prerequisites != null)
            {
                foreach (var prerequisiteKey in flag.Prerequisites)
                {
                    JsonVariationDetail(prerequisiteKey, LdValue.Null);
                }
            }

            EvaluationDetail<T> result;
            LdValue valueJson;
            if (flag.Value.IsNull)
            {
                valueJson = defaultJson;
                result = new EvaluationDetail<T>(defaultValue, flag.Variation,
                    flag.Reason ?? EvaluationReason.OffReason);
            }
            else
            {
                if (checkType && !defaultJson.IsNull && flag.Value.Type != defaultJson.Type)
                {
                    valueJson = defaultJson;
                    result = new EvaluationDetail<T>(defaultValue, null,
                        EvaluationReason.ErrorReason(EvaluationErrorKind.WrongType));
                }
                else
                {
                    valueJson = flag.Value;
                    result = new EvaluationDetail<T>(converter.ToType(flag.Value), flag.Variation,
                        flag.Reason ?? EvaluationReason.OffReason);
                }
            }

            var featureEvent = eventFactory.NewEvaluationEvent(featureKey, flag, Context,
                new EvaluationDetail<LdValue>(valueJson, flag.Variation, flag.Reason ?? EvaluationReason.OffReason),
                defaultJson);
            SendEvaluationEventIfOnline(featureEvent);
            return result;
        }

        private void SendEvaluationEventIfOnline(EventProcessorTypes.EvaluationEvent e)
        {
            EventProcessorIfEnabled().RecordEvaluationEvent(e);
        }

        /// <inheritdoc/>
        public IDictionary<string, LdValue> AllFlags()
        {
            var data = _dataStore.GetAll();
            if (data is null)
            {
                return ImmutableDictionary<string, LdValue>.Empty;
            }

            return data.Value.Items.Where(entry => entry.Value.Item != null)
                .ToDictionary(p => p.Key, p => p.Value.Item.Value);
        }

        /// <inheritdoc/>
        public void Track(string eventName, LdValue data, double metricValue)
        {
            EventProcessorIfEnabled().RecordCustomEvent(new EventProcessorTypes.CustomEvent
            {
                Timestamp = UnixMillisecondTime.Now,
                EventKey = eventName,
                Context = Context,
                Data = data,
                MetricValue = metricValue
            });
        }

        /// <inheritdoc/>
        public void Track(string eventName, LdValue data)
        {
            EventProcessorIfEnabled().RecordCustomEvent(new EventProcessorTypes.CustomEvent
            {
                Timestamp = UnixMillisecondTime.Now,
                EventKey = eventName,
                Context = Context,
                Data = data
            });
        }

        /// <inheritdoc/>
        public void Track(string eventName) =>
            Track(eventName, LdValue.Null);

        /// <inheritdoc/>
        public void Flush() =>
            _eventProcessor.Flush(); // eventProcessor will ignore this if it is offline

        /// <inheritdoc/>
        public bool FlushAndWait(TimeSpan timeout) =>
            _eventProcessor.FlushAndWait(timeout);

        /// <inheritdoc/>
        public Task<bool> FlushAndWaitAsync(TimeSpan timeout) =>
            _eventProcessor.FlushAndWaitAsync(timeout);

        /// <inheritdoc/>
        public bool Identify(Context context, TimeSpan maxWaitTime)
        {
            return AsyncUtils.WaitSafely(() => IdentifyAsync(context), maxWaitTime);
        }

        /// <inheritdoc/>
        public async Task<bool> IdentifyAsync(Context context)
        {
            Context newContext = _anonymousKeyContextDecorator.DecorateContext(context);
            if (_config.AutoEnvAttributes)
            {
                newContext = _autoEnvContextDecorator.DecorateContext(newContext);
            }

            Context
                oldContext =
                    newContext; // this initialization is overwritten below, it's only here to satisfy the compiler

            LockUtils.WithWriteLock(_stateLock, () =>
            {
                oldContext = _context;
                _context = newContext;
            });

            // If we had cached data for the new context, set the current in-memory flag data state to use
            // that data, so that any Variation calls made before Identify has completed will use the
            // last known values. If we did not have cached data, then we update the current in-memory
            // state to reflect that there is no flag data, so that Variation calls done before completion
            // will receive default values rather than the previous context's values. This does not modify
            // any flags in persistent storage, and (currently) it does *not* trigger any FlagValueChanged
            // events from FlagTracker.
            var cachedData = _dataStore.GetCachedData(newContext);
            if (cachedData != null)
            {
                _log.Debug("Identify found cached flag data for the new context");
            }

            _dataStore.Init(
                newContext,
                cachedData ?? new DataStoreTypes.FullDataSet(null),
                false // false means "don't rewrite the flags to persistent storage"
            );

            EventProcessorIfEnabled().RecordIdentifyEvent(new EventProcessorTypes.IdentifyEvent
            {
                Timestamp = UnixMillisecondTime.Now,
                Context = newContext
            });

            return await _connectionManager.SetContext(newContext);
        }

        /// <summary>
        /// Permanently shuts down the SDK client.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method closes all network collections, shuts down all background tasks, and releases any other
        /// resources being held by the SDK.
        /// </para>
        /// <para>
        /// If there are any pending analytics events, and if the SDK is online, it attempts to deliver the events
        /// to LaunchDarkly before closing.
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        void Dispose(bool disposing)
        {
            if (disposing)
            {
                _log.Info("Shutting down the LaunchDarkly client");

                _dataSourceUpdateSink.UpdateStatus(DataSourceState.Shutdown, null);

                _backgroundModeManager.BackgroundModeChanged -= OnBackgroundModeChanged;
                _connectionManager.Dispose();
                _dataStore.Dispose();
                _eventProcessor.Dispose();

                // Reset the static Instance to null *if* it was referring to this instance
                DetachInstance();
            }
        }

        internal void DetachInstance() // exposed for testing
        {
            Interlocked.CompareExchange(ref _instance, null, this);
        }

        internal void OnBackgroundModeChanged(object sender, BackgroundModeChangedEventArgs args) =>
            _connectionManager.SetInBackground(args.IsInBackground);

        // Returns our configured event processor (which might be the null implementation, if configured
        // with NoEvents)-- or, a stub if we have been explicitly put offline. This way, during times
        // when the application does not want any network activity, we won't bother buffering events.
        internal IEventProcessor EventProcessorIfEnabled() =>
            Offline ? ComponentsImpl.NullEventProcessor.Instance : _eventProcessor;
    }
}
