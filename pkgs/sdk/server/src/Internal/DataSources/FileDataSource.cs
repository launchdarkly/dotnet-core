using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Internal.FileLoading;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// The data source configured by <see cref="FileDataSourceBuilder"/>. It loads flag and
    /// segment data from local files and, with auto-update, reloads them when they change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The loading, retrying, and change detection are the shared <see cref="FileDataReloader"/>
    /// and <see cref="FileDataWatcher"/>. This class supplies what is particular to the file data
    /// source: every item of a successful load is stamped with one version number that counts the
    /// successful loads, every successful load replaces the whole data set, and the result of
    /// Start reports whether any load has ever succeeded.
    /// </para>
    /// <para>
    /// A load that fails leaves the data of the last successful load in place. With auto-update,
    /// a failed load is retried a bounded number of times, because a change notification can
    /// arrive while the file is still being written. Without it, files are loaded once per Start.
    /// </para>
    /// </remarks>
    internal sealed class FileDataSource : IDataSource
    {
        /// <summary>
        /// How many times a failing load is attempted with auto-update before the data source
        /// waits for the next detected change.
        /// </summary>
        internal const int MaxLoadAttempts = 5;

        /// <summary>
        /// How long to wait before a failed load is attempted again with auto-update.
        /// </summary>
        internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(600);

        private readonly IDataSourceUpdates _dataSourceUpdates;
        private readonly List<string> _paths;
        private readonly bool _autoUpdate;
        private readonly Logger _logger;
        private readonly FileDataReloader _reloader;

        // Guards the watcher and the disposed flag, so that a Start that races Dispose cannot
        // leave a watcher running.
        private readonly object _lock = new object();
        private FileDataWatcher _watcher;
        // Set when the watcher could not be constructed, so that the failure is logged once and not
        // on every Start.
        private bool _watcherFailed;
        private bool _disposed;

        private volatile bool _loadedValidData;
        // Read and written only in Apply, whose calls the reloader serializes.
        private volatile int _lastVersion;

        /// <summary>
        /// Constructs a file data source that loads flag and segment data from local files.
        /// </summary>
        /// <param name="dataSourceUpdates">receives the data set produced by each successful load</param>
        /// <param name="fileReader">reads file contents; injectable for testing</param>
        /// <param name="paths">the file paths to load, in order</param>
        /// <param name="autoUpdate">true to watch the files and reload on changes; also enables the
        /// bounded retry of loads that fail while a file is being written</param>
        /// <param name="alternateParser">optional parser for non-JSON content (for example YAML);
        /// null to parse JSON only</param>
        /// <param name="skipMissingPaths">true to skip missing files instead of failing the load</param>
        /// <param name="duplicateKeysHandling">how to handle a key that appears in more than one file</param>
        /// <param name="logger">the destination for log output</param>
        public FileDataSource(IDataSourceUpdates dataSourceUpdates, FileDataTypes.IFileReader fileReader,
            List<string> paths, bool autoUpdate, Func<string, object> alternateParser, bool skipMissingPaths,
            FileDataTypes.DuplicateKeysHandling duplicateKeysHandling,
            Logger logger)
        {
            _logger = logger;
            _dataSourceUpdates = dataSourceUpdates;
            _autoUpdate = autoUpdate;
            // The paths are kept as configured: a custom file reader receives them as given, and
            // the watcher resolves them against the current directory itself.
            _paths = new List<string>(paths);
            _reloader = new FileDataReloader(new FileDataReloaderConfig
            {
                Paths = _paths,
                DuplicateKeysHandling = duplicateKeysHandling == FileDataTypes.DuplicateKeysHandling.Ignore ?
                    FileDataDuplicateKeysHandling.Ignore : FileDataDuplicateKeysHandling.Fail,
                SkipMissingPaths = skipMissingPaths,
                // As before, SkipMissingPaths covers a file that is missing from a directory that
                // exists. A path whose directory does not exist fails the load, so a directory that
                // disappears cannot empty the store.
                FailOnMissingDirectory = true,
                Logger = logger,
                FileReader = fileReader,
                AlternateParser = alternateParser,
                // The version given here is replaced in Apply, like the version of every other item.
                FlagValueExpander = (key, value) =>
                    FileDataParser.MakeFallthroughFlagWithValue(key, value, 0),
                Apply = Apply,
                // Without auto-update, files are documented to be loaded only once, so a failure
                // is not retried in the background and the result of Start is final.
                DebounceDelay = autoUpdate ? FileDataReloader.DefaultDebounceDelay : TimeSpan.Zero,
                RetryDelay = autoUpdate ? RetryDelay : TimeSpan.Zero,
                MaxLoadAttempts = autoUpdate ? MaxLoadAttempts : 0,
                // Every successful load is applied, even when the files did not change.
                SkipUnchanged = false
            });
        }

        public Task<bool> Start()
        {
            if (!IsDisposed())
            {
                if (_autoUpdate)
                {
                    EnsureWatcher();
                }
                // Synchronous: Apply has run, or the failure has been logged, when this returns.
                _reloader.ReloadNow();
            }

            // We always complete the start task regardless of whether we successfully loaded data or not;
            // if the data files were bad, they're unlikely to become good within the short interval that
            // LdClient waits on this task, even if auto-updating is on.
            return Task.FromResult(_loadedValidData);
        }

        public bool Initialized => _loadedValidData;

        public void Dispose()
        {
            FileDataWatcher watcher;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                watcher = _watcher;
                _watcher = null;
            }
            // The watcher goes first so that it cannot signal a reloader that is being stopped.
            watcher?.Dispose();
            _reloader.Dispose();
        }

        private bool IsDisposed()
        {
            lock (_lock)
            {
                return _disposed;
            }
        }

        // Creates the watcher on the first Start. It is created before the initial load so that a
        // change made during the load is not lost. A directory that cannot be watched yet is the
        // watcher's own business; only a failure to construct it lands here, and the data source
        // then runs without change detection, as it always has.
        private void EnsureWatcher()
        {
            lock (_lock)
            {
                if (_disposed || _watcher != null || _watcherFailed)
                {
                    return;
                }
                try
                {
                    _watcher = new FileDataWatcher(_paths, OnChangeDetected, _logger);
                }
                catch (Exception e)
                {
                    _watcherFailed = true;
                    LogHelpers.LogException(_logger, "Unable to watch files for auto-updating", e);
                }
            }
        }

        private void OnChangeDetected()
        {
            if (IsDisposed())
            {
                return;
            }
            _logger.Info("detected file modification, reloading");
            _reloader.Trigger();
        }

        // Called by the reloader with each successfully merged result. Calls are serialized, so
        // the version counter advances once per successful load, in order.
        private void Apply(FileDataMergeResult merged)
        {
            // The version is consumed only once Init has accepted the data. An Init that throws is
            // a failed load, and the next successful load gets this version.
            var version = _lastVersion + 1;
            var flags = new List<KeyValuePair<string, ItemDescriptor>>(merged.Flags.Count);
            foreach (var kv in merged.Flags)
            {
                var flag = FlagWithVersion((FeatureFlag)kv.Value.Item, version);
                flags.Add(new KeyValuePair<string, ItemDescriptor>(kv.Key,
                    new ItemDescriptor(version, flag)));
            }
            var segments = new List<KeyValuePair<string, ItemDescriptor>>(merged.Segments.Count);
            foreach (var kv in merged.Segments)
            {
                var segment = SegmentWithVersion((Segment)kv.Value.Item, version);
                segments.Add(new KeyValuePair<string, ItemDescriptor>(kv.Key,
                    new ItemDescriptor(version, segment)));
            }
            // Both kinds are always present, so a load with no segments clears the segments.
            var allData = new FullDataSet<ItemDescriptor>(
                ImmutableDictionary.Create<DataKind, KeyedItems<ItemDescriptor>>()
                    .SetItem(DataModel.Features, new KeyedItems<ItemDescriptor>(flags))
                    .SetItem(DataModel.Segments, new KeyedItems<ItemDescriptor>(segments))
            );
            _dataSourceUpdates.Init(allData);
            _lastVersion = version;
            _loadedValidData = true;
        }

        private static FeatureFlag FlagWithVersion(FeatureFlag flag, int version) =>
            flag.Version == version ? flag :
            new FeatureFlag(
                flag.Key,
                version,
                flag.Deleted, flag.On, flag.Prerequisites, flag.Targets, flag.ContextTargets, flag.Rules, flag.Fallthrough,
                flag.OffVariation, flag.Variations, flag.Salt, flag.TrackEvents, flag.TrackEventsFallthrough,
                flag.DebugEventsUntilDate, flag.ClientSide, flag.SamplingRatio, flag.ExcludeFromSummaries, flag.Migration);

        private static Segment SegmentWithVersion(Segment segment, int version) =>
            segment.Version == version ? segment :
            new Segment(
                segment.Key,
                version,
                segment.Deleted, segment.Included, segment.Excluded, segment.IncludedContexts, segment.ExcludedContexts,
                segment.Rules, segment.Salt, segment.Unbounded, segment.UnboundedContextKind, segment.Generation);
    }
}
