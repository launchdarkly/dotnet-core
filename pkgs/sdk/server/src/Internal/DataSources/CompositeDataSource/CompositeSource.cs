using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// A composite source is a source that can dynamically switch between sources with the
    /// help of a list of <see cref="ISourceFactory"/> instances.
    /// </summary>
    internal sealed class CompositeSource : IDataSource, ICompositeSourceActionable
    {
        private readonly IDataSourceUpdates _sanitizedUpdateSink;
        private readonly SourcesList<(ISourceFactory Factory, ActionGenerator ActionGenerator)> _sourcesList;
        private readonly DataSourceUpdatesProxyTracker _proxyDataSourceUpdates;

        // Tracks the entry from the sources list that was used to create the current
        // data source instance. This allows operations such as blacklist to remove
        // the correct factory/mapper tuple from the list.
        private (ISourceFactory Factory, ActionGenerator ActionGenerator) _current;

        private IDataSource _currentDataSource;

        /// <summary>
        /// Creates a new <see cref="CompositeSource"/>.
        /// </summary>
        /// <param name="updatesSink">the sink that receives updates from the active source</param>
        /// <param name="factoriesAndGenerators">the ordered list of source factories and their associated action generators</param>
        /// <param name="circular">whether to loop off the end of the list back to the start when fallback occurs</param>
        public CompositeSource(
            IDataSourceUpdates updatesSink,
            IList<(ISourceFactory Factory, ActionGenerator ActionGenerator)> factoriesAndGenerators,
            bool circular = true,
            )
        {
            _sanitizedUpdateSink = new SanitizingDataSourceUpdates(updatesSink) ?? throw new ArgumentNullException(nameof(updatesSink));
            if (factoriesAndGenerators is null)
            {
                throw new ArgumentNullException(nameof(factoriesAndGenerators));
            }

            // this proxy is used to disconnect the current source from the updates sink when it is no longer wanted.
            _proxyDataSourceUpdates = new DataSourceUpdatesProxy(_sanitizedUpdateSink);

            _sourcesList = new SourcesList<(ISourceFactory Factory, DataSourceUpdatesToActionMapper Mapper)>(
                circular: circular,
                initialList: factoriesAndGenerators
                );
        }

        /// <summary>
        /// When <see cref="Start"/> is called, the current data source is started.
        /// </summary>
        /// <returns>
        /// a task that completes when the underlying current source has finished starting
        /// </returns>
        public Task<bool> Start()
        {
            return StartCurrent();
        }

        /// <summary>
        /// Returns whether the current underlying data source has finished initializing.
        /// </summary>
        public bool Initialized => _currentDataSource?.Initialized ?? false;

        public void Dispose()
        {
            DisposeCurrent();
        }

        private void TryFindNext()
        {
            if (_currentDataSource != null)
            {
                return;
            }

            var entry = _sourcesList.Next();
            if (entry.Factory == null)
            {
                return;
            }

            // Here we create a proxy and give that to the underlying data source.  This allows us to cut
            // the underlying data source off from the updates sink as a defensive manner when the data
            // source is no longer wanted.
            IDataSourceUpdates proxiedUpdates = _proxyDataSourceUpdates.NewProxy();
            
            _current = entry;
            // For now we pass the original updates sink directly; in future enhancements the
            // mapper and proxy can be used to route updates and actions.
            _currentDataSource = entry.Factory.CreateSource(proxiedUpdates);
        }

        private void HandleAction(CompositeSourceAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            action.Accept(this);
        }

        #region ICompositeSourceActionable

        public void StartCurrent()
        {
            TryFindNext();
            if (_currentDataSource is null)
            {
                // No sources available.
                return Task.FromResult(false);
            }

            var result = _currentDataSource.Start();
            // spoof initializing (if the underlying source reported initializing, sanitizer will not report it again)
            _sanitizedUpdateSink.UpdateStatus(DataSourceState.Initializing, null);
            return result;
        }

        public void DisposeCurrent()
        {
            _currentDataSource?.Dispose();
            _currentDataSource = null;

            // cut off all the update proxies that have been handed out
            _proxyDataSourceUpdates.DisableExistingProxies();

            // spoof interrupted (if the underlying source reported interrupted, sanitizer will not report it again)
            _sanitizedUpdateSink.UpdateStatus(DataSourceState.Interrupted, null);
        }

        public void GoToNext()
        {
            // moving always disconnects the current source
            DisconnectCurrent();
            
            TryFindNext();
            if (_currentDataSource is null)
            {
                // No more sources available.
                return Task.FromResult(false);
            }
        }

        public void GoToFirst()
        {
            // moving always disconnects the current source
            DisconnectCurrent();
            _sourcesList.Reset();
            TryFindNext();
            if (_currentDataSource is null)
            {
                // No sources available.
                return Task.FromResult(false);
            }
        }

        public void BlacklistCurrent()
        {
            if (_current != null)
            {
                _sourcesList.Remove(_current);
                _current = default;
            }
        }

        #endregion
    }
}


