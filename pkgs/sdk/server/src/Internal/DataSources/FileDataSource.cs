using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
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
    internal sealed class FileDataSource : IDataSource
    {
        private readonly IDataSourceUpdates _dataSourceUpdates;
        private readonly List<string> _paths;
        private readonly bool _autoUpdate;
        private readonly Logger _logger;
        private readonly FileDataReloader _reloader;
        private readonly object _lifecycleLock = new object();
        private FileDataWatcher _watcher;
        private volatile bool _loadedValidData;
        private volatile bool _disposed;
        private int _lastVersion;

        /// <summary>
        /// Constructs a file data source that loads flag and segment data from local files.
        /// </summary>
        /// <param name="dataSourceUpdates">receives the data set produced by each successful load</param>
        /// <param name="fileReader">reads file contents; injectable for testing</param>
        /// <param name="paths">the file paths to load, in order</param>
        /// <param name="autoUpdate">true to watch the files and reload on changes; this also enables
        /// the retry of loads that fail while a file is being written</param>
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
            _paths = new List<string>(paths);
            _autoUpdate = autoUpdate;
            _lastVersion = 0;
            _reloader = new FileDataReloader(new FileDataReloaderConfig
            {
                Paths = _paths,
                DuplicateKeysHandling = ToMergeHandling(duplicateKeysHandling),
                SkipMissingPaths = skipMissingPaths,
                Logger = logger,
                FileReader = fileReader,
                AlternateParser = alternateParser,
                // The version is assigned when the data is applied, so the expanded flag gets a
                // placeholder here.
                FlagValueExpander = (key, value) => FileDataParser.MakeFallthroughFlagWithValue(key, value, 0),
                Apply = ApplyData,
                // With auto-update off, files are documented to be loaded only once, so a failed load
                // is not retried in the background. The result of Start stays final.
                DebounceDelay = autoUpdate ? FileDataReloader.DefaultDebounceDelay : TimeSpan.Zero,
                RetryDelay = autoUpdate ? FileDataReloader.DefaultRetryDelay : TimeSpan.Zero,
                SkipUnchanged = true
            });
        }

        public Task<bool> Start()
        {
            if (_autoUpdate)
            {
                lock (_lifecycleLock)
                {
                    if (_watcher == null && !_disposed)
                    {
                        try
                        {
                            _watcher = new FileDataWatcher(_paths, _reloader.Trigger, _logger);
                        }
                        catch (Exception e)
                        {
                            LogHelpers.LogException(_logger, "Unable to watch files for auto-updating", e);
                        }
                    }
                }
            }
            _reloader.ReloadNow();

            // We always complete the start task regardless of whether we successfully loaded data or not;
            // if the data files were bad, they're unlikely to become good within the short interval that
            // LdClient waits on this task, even if auto-updating is on.
            TaskCompletionSource<bool> initTask = new TaskCompletionSource<bool>();
            initTask.SetResult(_loadedValidData);
            return initTask.Task;
        }

        public bool Initialized => _loadedValidData;

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_lifecycleLock)
                {
                    _disposed = true;
                    _watcher?.Dispose();
                    _watcher = null;
                }
                _reloader.Dispose();
            }
        }

        // Receives each successfully merged data set from the reloader. Every item gets the same
        // new version number, so a reload that changes a flag's content is seen as a change by the
        // data store and by flag change listeners.
        private void ApplyData(FileDataMergeResult merged)
        {
            var version = Interlocked.Increment(ref _lastVersion);
            var flags = ImmutableList.CreateBuilder<KeyValuePair<string, ItemDescriptor>>();
            foreach (var kv in merged.Flags)
            {
                var flag = FileDataParser.FlagWithVersion((FeatureFlag)kv.Value.Item, version);
                flags.Add(new KeyValuePair<string, ItemDescriptor>(kv.Key, new ItemDescriptor(version, flag)));
            }
            var segments = ImmutableList.CreateBuilder<KeyValuePair<string, ItemDescriptor>>();
            foreach (var kv in merged.Segments)
            {
                var segment = FileDataParser.SegmentWithVersion((Segment)kv.Value.Item, version);
                segments.Add(new KeyValuePair<string, ItemDescriptor>(kv.Key, new ItemDescriptor(version, segment)));
            }
            var allData = new FullDataSet<ItemDescriptor>(
                ImmutableDictionary.Create<DataKind, KeyedItems<ItemDescriptor>>()
                    .SetItem(DataModel.Features, new KeyedItems<ItemDescriptor>(flags.ToImmutable()))
                    .SetItem(DataModel.Segments, new KeyedItems<ItemDescriptor>(segments.ToImmutable()))
            );
            _dataSourceUpdates.Init(allData);
            _loadedValidData = true;
        }

        private static FileDataDuplicateKeysHandling ToMergeHandling(FileDataTypes.DuplicateKeysHandling handling)
        {
            switch (handling)
            {
                case FileDataTypes.DuplicateKeysHandling.Throw:
                    return FileDataDuplicateKeysHandling.Fail;
                case FileDataTypes.DuplicateKeysHandling.Ignore:
                    return FileDataDuplicateKeysHandling.Ignore;
                default:
                    throw new ArgumentOutOfRangeException(nameof(handling), handling,
                        "Unknown duplicate keys handling");
            }
        }
    }
}
