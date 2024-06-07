using System;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Interfaces;
using LaunchDarkly.Sdk.Client.Internal.Events;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    /// <summary>
    /// Manages our connection to LaunchDarkly, if any, and encapsulates all of the state that
    /// determines whether we should have a connection or not.
    /// </summary>
    /// <remarks>
    /// Whenever the state of this object is modified by <see cref="SetForceOffline(bool)"/>,
    /// <see cref="SetNetworkEnabled(bool)"/>, <see cref="SetInBackground(bool)"/>,
    /// <see cref="SetContext(Context)"/>, or <see cref="Start"/>, it will decide whether to make a new
    /// connection, drop an existing connection, both, or neither. If the caller wants to know when a
    /// new connection (if any) is ready, it should <c>await</c> the returned task.
    ///
    /// ConnectionManager also keeps track of whether event sending should be enabled.
    ///
    /// The object begins in a non-started state, so regardless of what properties are set, it will not
    /// make a connection until after <see cref="Start"/> has been called.
    /// </remarks>
    internal sealed class ConnectionManager : IDisposable
    {
        private readonly Logger _log;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly LdClientContext _clientContext;
        private readonly IComponentConfigurer<IDataSource> _dataSourceFactory;
        private readonly IDataSourceUpdateSink _updateSink;
        private readonly IEventProcessor _eventProcessor;
        private readonly DiagnosticDisablerImpl _diagnosticDisabler;
        private readonly bool _enableBackgroundUpdating;
        private bool _disposed = false;
        private bool _started = false;
        private bool _initialized = false;
        private bool _forceOffline = false;
        private bool _networkEnabled = false;
        private bool _inBackground = false;
        private Context _context;
        private IDataSource _dataSource = null;

        // Note that these properties do not have simple setter methods, because the setters all
        // need to return Tasks.

        /// <summary>
        /// True if we are in offline mode (<see cref="SetForceOffline(bool)"/> was set to true).
        /// </summary>
        public bool ForceOffline => LockUtils.WithReadLock(_lock, () => _forceOffline);

        /// <summary>
        /// True if we have been told there is network connectivity (<see cref="SetNetworkEnabled(bool)"/>
        /// was set to true).
        /// </summary>
        public bool NetworkEnabled => LockUtils.WithReadLock(_lock, () => _networkEnabled);

        /// <summary>
        /// True if we made a successful LaunchDarkly connection or do not need to make one (see
        /// <see cref="LdClient.Initialized"/>).
        /// </summary>
        public bool Initialized => LockUtils.WithReadLock(_lock, () => _initialized);

        internal ConnectionManager(
            LdClientContext clientContext,
            IComponentConfigurer<IDataSource> dataSourceFactory,
            IDataSourceUpdateSink updateSink,
            IEventProcessor eventProcessor,
            DiagnosticDisablerImpl diagnosticDisabler,
            bool enableBackgroundUpdating,
            Context initialContext,
            Logger log
            )
        {
            _clientContext = clientContext;
            _dataSourceFactory = dataSourceFactory;
            _updateSink = updateSink;
            _eventProcessor = eventProcessor;
            _diagnosticDisabler = diagnosticDisabler;
            _enableBackgroundUpdating = enableBackgroundUpdating;
            _context = initialContext;
            _log = log;
        }

        /// <summary>
        /// Sets whether the client should always be offline, and attempts to connect if appropriate.
        /// </summary>
        /// <remarks>
        /// Besides updating the value of the <see cref="ForceOffline"/> property, we do the
        /// following:
        /// 
        /// If <c>forceOffline</c> is true, we drop our current connection (if any), and we will not
        /// make any connections no matter what other properties are changed as long as this property is
        /// still true.
        ///
        /// If <c>forceOffline</c> is false and we already have a connection, nothing happens.
        ///
        /// If <c>forceOffline</c> is false and we have no connection, but other conditions disallow
        /// making a connection (or we do not have an update processor factory), nothing happens.
        ///
        /// If <c>forceOffline</c> is false, and we do not yet have a connection, and no other
        /// conditions disallow making a connection, and we have an update processor factory,
        /// we create an update processor and tell it to start.
        ///
        /// The returned task is immediately completed unless we are making a new connection, in which
        /// case it is completed when the update processor signals success or failure. The task yields
        /// a true result if we successfully made a connection <i>or</i> if we decided not to connect
        /// because we are in offline mode. In other words, the result is true if
        /// <see cref="Initialized"/> is true.
        /// </remarks>
        /// <param name="forceOffline">true if the client should always be offline</param>
        /// <returns>a task as described above</returns>
        public Task<bool> SetForceOffline(bool forceOffline)
        {
            return LockUtils.WithWriteLock(_lock, () =>
            {
                if (_disposed || _forceOffline == forceOffline)
                {
                    return Task.FromResult(false);
                }
                _forceOffline = forceOffline;
                _log.Info("Offline mode is now {0}", forceOffline);
                return OpenOrCloseConnectionIfNecessary(false); // not awaiting
            });
        }

        /// <summary>
        /// Sets whether we should be able to make network connections, and attempts to connect if appropriate.
        /// </summary>
        /// <remarks>
        /// Besides updating the value of the <see cref="NetworkEnabled"/> property, we do the
        /// following:
        /// 
        /// If <c>networkEnabled</c> is false, we drop our current connection (if any), and we will not
        /// make any connections no matter what other properties are changed as long as this property is
        /// still true.
        ///
        /// If <c>networkEnabled</c> is true and we already have a connection, nothing happens.
        ///
        /// If <c>networkEnabled</c> is true and we have no connection, but other conditions disallow
        /// making a connection (or we do not have an update processor factory), nothing happens.
        ///
        /// If <c>networkEnabled</c> is true, and we do not yet have a connection, and no other
        /// conditions disallow making a connection, and we have an update processor factory,
        /// we create an update processor and tell it to start.
        ///
        /// The returned task is immediately completed unless we are making a new connection, in which
        /// case it is completed when the update processor signals success or failure. The task yields
        /// a true result if we successfully made a connection <i>or</i> if we decided not to connect
        /// because we are in offline mode. In other words, the result is true if
        /// <see cref="Initialized"/> is true.
        /// </remarks>
        /// <param name="networkEnabled">true if we think we can make network connections</param>
        /// <returns>a task as described above</returns>
        public Task<bool> SetNetworkEnabled(bool networkEnabled)
        {
            return LockUtils.WithWriteLock(_lock, () =>
            {
                if (_disposed || _networkEnabled == networkEnabled)
                {
                    return Task.FromResult(false);
                }
                _networkEnabled = networkEnabled;
                _log.Info("Network availability is now {0}", networkEnabled);
                return OpenOrCloseConnectionIfNecessary(false); // not awaiting
            });
        }

        /// <summary>
        /// Sets whether the application is currently in the background.
        /// </summary>
        /// <remarks>
        /// When in the background, we use a different data source (polling, at a longer interval)
        /// and we do not send diagnostic events.
        /// </remarks>
        /// <param name="inBackground">true if the application is now in the background</param>
        public void SetInBackground(bool inBackground)
        {
            LockUtils.WithWriteLock(_lock, () =>
            {
                if (_disposed || _inBackground == inBackground)
                {
                    return;
                }
                _inBackground = inBackground;
                _log.Debug("Background mode is changing to {0}", inBackground);
                _ = OpenOrCloseConnectionIfNecessary(true); // not awaiting
            });
        }

        /// <summary>
        /// Updates the current user.
        /// </summary>
        /// <param name="context">the new context</param>
        /// <returns>a task that is completed when we have received data for the new user, if the
        /// data source is online, or completed immediately otherwise</returns>
        public Task<bool> SetContext(Context context)
        {
            return LockUtils.WithWriteLock(_lock, () =>
            {
                if (_disposed)
                {
                    return Task.FromResult(false);
                }
                _context = context;
                _initialized = false;
                return OpenOrCloseConnectionIfNecessary(true);
            });
        }

        /// <summary>
        /// Tells the <c>ConnectionManager</c> that it can go ahead and connect if appropriate.
        /// </summary>
        /// <returns>a task which will yield true if this method results in a successful connection, or
        /// if we are in offline mode and don't need to make a connection</returns>
        public Task<bool> Start()
        {
            return LockUtils.WithWriteLock(_lock, () =>
            {
                if (_started)
                {
                    return Task.FromResult(_initialized);
                }
                _started = true;
                return OpenOrCloseConnectionIfNecessary(false); // not awaiting
            });
        }

        public void Dispose()
        {
            IDataSource dataSource = null;
            LockUtils.WithWriteLock(_lock, () =>
            {
                if (_disposed)
                {
                    return;
                }
                dataSource = _dataSource;
                _dataSource = null;
                _disposed = true;
            });
            dataSource?.Dispose();
        }

        // This method is called while _lock is being held. If we're starting up a new connection, we do
        // *not* wait for it to succeed; we return a Task that will be completed once it succeeds. In all
        // other cases we return an immediately-completed Task.

        private Task<bool> OpenOrCloseConnectionIfNecessary(bool mustReinitializeDataSource)
        {
            if (!_started)
            {
                return Task.FromResult(false);
            }

            // Analytics event sending is enabled as long as we're allowed to do any network things.
            // (If the SDK is configured not to send events, then this is a no-op because _eventProcessor
            // will be a no-op implementation).
            _eventProcessor.SetOffline(_forceOffline || !_networkEnabled);

            // Diagnostic events are disabled if we're in the background.
            _diagnosticDisabler?.SetDisabled(_forceOffline || !_networkEnabled || _inBackground);

            if (mustReinitializeDataSource && _dataSource != null)
            {
                _dataSource?.Dispose();
                _dataSource = null;
            }

            if (_networkEnabled && !_forceOffline)
            {
                if (_inBackground && !_enableBackgroundUpdating)
                {
                    _log.Debug("Background updating is disabled");
                    _updateSink.UpdateStatus(DataSourceState.BackgroundDisabled, null);
                    return Task.FromResult(true);
                }
                if (_dataSource is null)
                {
                    // Set the state to Initializing when there's a new data source that has not yet
                    // started. The state will then be updated as appropriate by the data source either
                    // calling UpdateStatus, or Init which implies UpdateStatus(Valid).
                    _updateSink.UpdateStatus(DataSourceState.Initializing, null);
                    _dataSource = _dataSourceFactory.Build(
                        _clientContext.WithContextAndBackgroundState(_context, _inBackground)
                            .WithDataSourceUpdateSink(_updateSink));
                    return _dataSource.Start()
                        .ContinueWith(SetInitializedIfUpdateProcessorStartedSuccessfully);
                }
            }
            else
            {
                // Either we've been explicitly set to be offline (in which case the state is always
                // SetOffline regardless of any other conditions), or we're offline because the network
                // is unavailable. If either of those things changes, we'll end up calling this method
                // again and the state will be updated if appropriate.
                _dataSource?.Dispose();
                _dataSource = null;
                _initialized = true;
                _updateSink.UpdateStatus(
                    _forceOffline ? DataSourceState.SetOffline : DataSourceState.NetworkUnavailable,
                    null
                    );
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        // When this method is called, we are no longer holding the lock.

        private bool SetInitializedIfUpdateProcessorStartedSuccessfully(Task<bool> task)
        {
            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    // Don't let exceptions from the update processor propagate up into the SDK. Just say we didn't initialize.
                    LogHelpers.LogException(_log, "Failed to initialize LaunchDarkly connection", task.Exception);
                    return false;
                }
                var success = task.Result;
                if (success)
                {
                    LockUtils.WithWriteLock(_lock, () =>
                    {
                        _initialized = true;
                    });
                    return true;
                }
            }
            return false;
        }
    }
}
