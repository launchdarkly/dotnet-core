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
        private readonly Logger _logger;
        private volatile bool _started;
        private volatile bool _loadedValidData;
        private volatile int _lastVersion;
        private object _updateLock = new object();

        private const int MaxRetries = 5;
        private readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(0.6);
        private readonly Dictionary<string, int> _retryCounts = new Dictionary<string, int>();

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
            LoadAll();

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
                _reloader?.Dispose();
            }
        }

        private void LoadAll()
        {
            lock (_updateLock)
            {
                var version = Interlocked.Increment(ref _lastVersion);
                var flags = new Dictionary<string, ItemDescriptor>();
                var segments = new Dictionary<string, ItemDescriptor>();
                foreach (var path in _paths)
                {
                    try
                    {
                        var content = _fileReader.ReadAllText(path);
                        _logger.Debug("file data: {0}", content);
                        var data = _parser.Parse(content, version);
                        _dataMerger.AddToData(data, flags, segments);
                        // Remove any retry count associated with this path.
                        _retryCounts.Remove(path);
                    }
                    catch (FileNotFoundException) when (_skipMissingPaths)
                    {
                        _logger.Debug("{0}: {1}", path, "File not found");
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // We may have received the notification of a file change while the file was being written.
                        // So we may read an empty or partially written file. So, when we encounter a JSON parsing issue
                        // we will retry after a short delay.
                        // We will retry up to MaxRetries times before giving up.
                        if (!_retryCounts.ContainsKey(path))
                        {
                            _retryCounts[path] = 0;
                        }
                        _retryCounts[path]++;

                        if (_retryCounts[path] < MaxRetries)
                        {
                            _logger.Warn("{0}: {1}", path, "Failed to parse file, retrying in " + RetryDelay.TotalMilliseconds + " milliseconds");
                            Task.Run(async () =>
                            {
                                await Task.Delay(RetryDelay);
                                LoadAll();
                            });
                        }
                        else
                        {
                            _logger.Error("{0}: {1}", path, "Failed to parse file after " + MaxRetries + " retries");
                        }

                        return;
                    }
                    catch (Exception e)
                    {
                        LogHelpers.LogException(_logger, "Failed to load " + path, e);
                        return;
                    }
                }
                
                // If any files failed to load, from anything other than not existing, then that
                // update would fail. This behavior is retained with the addition of the retry. But it should be
                // examined.

                var allData = new FullDataSet<ItemDescriptor>(
                    ImmutableDictionary.Create<DataKind, KeyedItems<ItemDescriptor>>()
                        .SetItem(DataModel.Features, new KeyedItems<ItemDescriptor>(flags))
                        .SetItem(DataModel.Segments, new KeyedItems<ItemDescriptor>(segments))
                );
                _dataSourceUpdates.Init(allData);
                _loadedValidData = true;
            }
        }

        private void TriggerReload()
        {
            if (_started)
            {
                _logger.Info("detected file modification, reloading");
                LoadAll();
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
