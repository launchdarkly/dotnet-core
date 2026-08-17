using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    internal sealed class FileDataSource : IDataSource
    {
        private readonly IDataSourceUpdates _dataSourceUpdates;
        private readonly List<string> _paths;
        private readonly IDisposable _reloader;
        private readonly FlagFileParser _parser;
        private readonly FlagFileDataMerger _dataMerger;
        private readonly FileDataTypes.IFileReader _fileReader;
        private readonly bool _skipMissingPaths;
        private readonly bool _autoUpdate;
        private readonly Logger _logger;
        private volatile bool _started;
        private volatile bool _loadedValidData;
        private volatile bool _disposed;
        private volatile int _lastVersion;
        private object _updateLock = new object();

        private const int MaxParseAttempts = 5;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(600);
        // Consecutive parse failures per path within the current failure episode. A failure seen on
        // an externally triggered load (Start or a file-change notification) starts a new episode,
        // so the retry budget is per-episode, not per-lifetime. Only touched inside _updateLock.
        private readonly Dictionary<string, int> _parseFailureCounts = new Dictionary<string, int>();
        // Whether a delayed retry is already scheduled; at most one retry chain exists at a time,
        // since each retry re-reads every path anyway. Only touched inside _updateLock.
        private bool _retryPending;

        public FileDataSource(IDataSourceUpdates dataSourceUpdates, FileDataTypes.IFileReader fileReader,
            List<string> paths, bool autoUpdate, Func<string, object> alternateParser, bool skipMissingPaths,
            FileDataTypes.DuplicateKeysHandling duplicateKeysHandling,
            Logger logger)
        {
            _logger = logger;
            _dataSourceUpdates = dataSourceUpdates;
            _paths = new List<string>(paths);
            _parser = new FlagFileParser(alternateParser);
            _dataMerger = new FlagFileDataMerger(duplicateKeysHandling);
            _fileReader = fileReader;
            _skipMissingPaths = skipMissingPaths;
            _autoUpdate = autoUpdate;
            _lastVersion = 0;
            if (autoUpdate)
            {
                try
                {
                    _reloader = new FileWatchingReloader(_paths, TriggerReload);
                }
                catch (Exception e)
                {
                    LogHelpers.LogException(_logger, "Unable to watch files for auto-updating", e);
                    _reloader = null;
                }
            }
            else
            {
                _reloader = null;
            }
        }

        public Task<bool> Start()
        {
            _started = true;
            LoadAll(isRetry: false);

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
                _disposed = true;
                _reloader?.Dispose();
            }
        }

        private void LoadAll(bool isRetry)
        {
            lock (_updateLock)
            {
                if (_disposed)
                {
                    return;
                }
                var version = Interlocked.Increment(ref _lastVersion);
                var flags = new Dictionary<string, ItemDescriptor>();
                var segments = new Dictionary<string, ItemDescriptor>();
                foreach (var path in _paths)
                {
                    try
                    {
                        var content = _fileReader.ReadAllText(path);
                        _logger.Debug("file data: {0}", content);
                        FullDataSet<ItemDescriptor> data;
                        try
                        {
                            data = _parser.Parse(content, version);
                        }
                        catch (Exception e)
                        {
                            // A file-change notification can fire while the file is mid-write, so a parse
                            // failure may just mean we read an empty or partially written file. This applies
                            // to any configured parser (JSON or alternate), so we treat every failure of
                            // Parse — as opposed to reading the file — as potentially transient.
                            HandleParseFailure(path, e, isRetry);
                            return;
                        }
                        _dataMerger.AddToData(data, flags, segments);
                        _parseFailureCounts.Remove(path);
                    }
                    catch (FileNotFoundException) when (_skipMissingPaths)
                    {
                        _logger.Debug("{0}: {1}", path, "File not found");
                    }
                    catch (Exception e)
                    {
                        LogHelpers.LogException(_logger, "Failed to load " + path, e);
                        return;
                    }
                }

                var allData = new FullDataSet<ItemDescriptor>(
                    ImmutableDictionary.Create<DataKind, KeyedItems<ItemDescriptor>>()
                        .SetItem(DataModel.Features, new KeyedItems<ItemDescriptor>(flags))
                        .SetItem(DataModel.Segments, new KeyedItems<ItemDescriptor>(segments))
                );
                _dataSourceUpdates.Init(allData);
                _loadedValidData = true;
            }
        }

        // Called under _updateLock when parsing a path's content fails.
        private void HandleParseFailure(string path, Exception e, bool isRetry)
        {
            if (!_autoUpdate)
            {
                // With auto-update off, files are documented to be loaded only once, so we don't
                // retry in the background — Start()'s result stays final.
                LogHelpers.LogException(_logger, "Failed to parse " + path, e);
                return;
            }

            var attempts = 1;
            if (isRetry && _parseFailureCounts.TryGetValue(path, out var previousAttempts))
            {
                attempts = previousAttempts + 1;
            }

            if (attempts < MaxParseAttempts)
            {
                _parseFailureCounts[path] = attempts;
                _logger.Warn("{0}: failed to parse file ({1}); will retry in {2} ms in case it was incompletely written",
                    path, LogValues.ExceptionSummary(e), RetryDelay.TotalMilliseconds);
                ScheduleRetry();
            }
            else
            {
                _parseFailureCounts.Remove(path);
                LogHelpers.LogException(_logger,
                    string.Format("{0}: failed to parse file after {1} attempts", path, MaxParseAttempts), e);
            }
        }

        // Called under _updateLock.
        private void ScheduleRetry()
        {
            if (_retryPending)
            {
                return; // the already-scheduled retry will re-read every path
            }
            _retryPending = true;
            Task.Run(async () =>
            {
                await Task.Delay(RetryDelay).ConfigureAwait(false);
                try
                {
                    RetryLoadAll();
                }
                catch (Exception e)
                {
                    // Nothing observes this task, so any escaping exception would otherwise vanish.
                    LogHelpers.LogException(_logger, "Unexpected error while retrying file data load", e);
                }
            });
        }

        private void RetryLoadAll()
        {
            lock (_updateLock)
            {
                _retryPending = false;
                if (_disposed || _parseFailureCounts.Count == 0)
                {
                    // Disposed, or an externally triggered load already succeeded in the meantime
                    // (success clears the failure counts) — a reload would be redundant and would
                    // re-Init identical data at bumped versions, firing spurious change events.
                    return;
                }
                LoadAll(isRetry: true);
            }
        }

        private void TriggerReload()
        {
            if (_started)
            {
                _logger.Info("detected file modification, reloading");
                LoadAll(isRetry: false);
            }
        }
    }

    // Provides the logic for merging sets of feature flag and segment data.
    internal sealed class FlagFileDataMerger
    {
        private readonly FileDataTypes.DuplicateKeysHandling _duplicateKeysHandling;

        public FlagFileDataMerger(FileDataTypes.DuplicateKeysHandling duplicateKeysHandling)
        {
            _duplicateKeysHandling = duplicateKeysHandling;
        }

        public void AddToData(
            FullDataSet<ItemDescriptor> data,
            IDictionary<string, ItemDescriptor> flagsOut,
            IDictionary<string, ItemDescriptor> segmentsOut
            )
        {
            foreach (var kv0 in data.Data)
            {
                var kind = kv0.Key;
                foreach (var kv1 in kv0.Value.Items)
                {
                    var items = kind == DataModel.Segments ? segmentsOut : flagsOut;
                    var key = kv1.Key;
                    var item = kv1.Value;
                    if (items.ContainsKey(key))
                    {
                        switch (_duplicateKeysHandling)
                        {
                            case FileDataTypes.DuplicateKeysHandling.Throw:
                                throw new System.Exception("in \"" + kind.Name + "\", key \"" + key +
                                    "\" was already defined");
                            case FileDataTypes.DuplicateKeysHandling.Ignore:
                                break;
                            default:
                                throw new NotImplementedException("Unknown duplicate keys handling: " + _duplicateKeysHandling);
                        }
                    }
                    else
                    {
                        items[key] = item;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Implementation of file monitoring using FileSystemWatcher.
    /// </summary>
    internal sealed class FileWatchingReloader : IDisposable
    {
        private readonly ISet<string> _filePaths;
        private readonly Action _reload;
        private readonly List<FileSystemWatcher> _watchers;

        public FileWatchingReloader(List<string> paths, Action reload)
        {
            _reload = reload;

            _filePaths = new HashSet<string>();
            var dirPaths = new HashSet<string>();
            foreach (var p in paths)
            {
                var absPath = Path.GetFullPath(p);
                _filePaths.Add(absPath);
                var dirPath = Path.GetDirectoryName(absPath);
                dirPaths.Add(dirPath);
            }

            _watchers = new List<FileSystemWatcher>();
            foreach (var dir in dirPaths)
            {
                var w = new FileSystemWatcher(dir);

                w.Changed += (s, args) => ChangedPath(args.FullPath);
                w.Created += (s, args) => ChangedPath(args.FullPath);
                w.Renamed += (s, args) => ChangedPath(args.FullPath);
                w.EnableRaisingEvents = true;

                _watchers.Add(w);
            }
        }

        private void ChangedPath(string path)
        {
            if (_filePaths.Contains(path))
            {
                _reload();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var w in _watchers)
                {
                    w.Dispose();
                }
            }
        }
    }
}
