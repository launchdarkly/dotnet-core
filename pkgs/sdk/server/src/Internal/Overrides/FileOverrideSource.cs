using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Server.Integrations;
using LaunchDarkly.Sdk.Server.Internal.FileLoading;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.Overrides
{
    /// <summary>
    /// The file-based override source. It reads one or more files in the file data document format,
    /// supplies the merged result to the override sink as a complete snapshot, and reloads when the
    /// files change.
    /// </summary>
    internal sealed class FileOverrideSource : IOverrideSource
    {
        private readonly IReadOnlyList<string> _paths;
        private readonly FileOverrideTypes.DuplicateKeysHandling _duplicateKeysHandling;
        private readonly FileOverrideTypes.ChangeDetection _changeDetection;
        private readonly TimeSpan _pollInterval;
        private readonly Func<string, object> _parser;
        private readonly Logger _log;

        private FileDataReloader _reloader;
        private IDisposable _changeDetector;
        private int _disposed;

        internal FileOverrideSource(
            IReadOnlyList<string> paths,
            FileOverrideTypes.DuplicateKeysHandling duplicateKeysHandling,
            FileOverrideTypes.ChangeDetection changeDetection,
            TimeSpan pollInterval,
            Func<string, object> parser,
            Logger log
            )
        {
            _paths = paths;
            _duplicateKeysHandling = duplicateKeysHandling;
            _changeDetection = changeDetection;
            _pollInterval = pollInterval;
            _parser = parser;
            _log = log;
        }

        // Exposed for tests of the builder.
        internal FileOverrideTypes.ChangeDetection ChangeDetection => _changeDetection;
        internal TimeSpan PollInterval => _pollInterval;
        internal IReadOnlyList<string> Paths => _paths;
        internal FileOverrideTypes.DuplicateKeysHandling DuplicateKeysHandling => _duplicateKeysHandling;
        internal IDisposable ChangeDetector => _changeDetector;

        /// <summary>
        /// Performs the initial load synchronously, so overrides present in the files are in effect by
        /// the time the client constructor returns, and then starts the change detector. A file that
        /// does not exist yet contributes no overrides. A file that cannot be read or parsed is not
        /// fatal: the client runs with the last good overrides, the failure is logged, and the retry
        /// plus the change signal recover once the file is readable.
        /// </summary>
        public void Start(IOverrideSink sink)
        {
            _reloader = new FileDataReloader(new FileDataReloaderConfig
            {
                Paths = _paths,
                DuplicateKeysHandling = _duplicateKeysHandling == FileOverrideTypes.DuplicateKeysHandling.Ignore ?
                    FileDataDuplicateKeysHandling.Ignore : FileDataDuplicateKeysHandling.Fail,
                SkipMissingPaths = true,
                Logger = _log,
                AlternateParser = _parser,
                // A value-only entry behaves like a flag that is off and serves its single value.
                FlagValueExpander = (key, value) => FileDataParser.MakeOffFlagWithValue(key, value, 0),
                Apply = merged =>
                {
                    sink.SetOverrides(ToDataSet(merged));
                    LogOverridesInEffect(merged);
                },
                DebounceDelay = FileDataReloader.DefaultDebounceDelay,
                RetryDelay = FileDataReloader.DefaultRetryDelay,
                SkipUnchanged = true
            });
            _reloader.ReloadNow();

            switch (_changeDetection)
            {
                case FileOverrideTypes.ChangeDetection.Watching:
                    try
                    {
                        _changeDetector = new FileDataWatcher(_paths, _reloader.Trigger, _log);
                    }
                    catch (Exception e)
                    {
                        LogHelpers.LogException(_log, "Unable to watch override files", e);
                    }
                    break;
                default:
                    _changeDetector = new FileDataPoller(_paths, _pollInterval, _reloader.Trigger, _log);
                    break;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            _changeDetector?.Dispose();
            _reloader?.Dispose();
        }

        private static FullDataSet<ItemDescriptor> ToDataSet(FileDataMergeResult merged) =>
            new FullDataSet<ItemDescriptor>(ImmutableList.Create(
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(DataModel.Features,
                    new KeyedItems<ItemDescriptor>(merged.Flags)),
                new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(DataModel.Segments,
                    new KeyedItems<ItemDescriptor>(merged.Segments))));

        // Reports the overrides now in effect and the file each came from. The reloader applies a
        // snapshot only when the content changed, so this logs each change once.
        private void LogOverridesInEffect(FileDataMergeResult merged)
        {
            var details = new List<string>(merged.Files.Count);
            foreach (var file in merged.Files)
            {
                if (!file.Present)
                {
                    details.Add(file.Path + ": absent");
                }
                else if (file.Flags == 0 && file.Segments == 0)
                {
                    details.Add(file.Path + ": no entries");
                }
                else
                {
                    details.Add(file.Path + ": " + CountsText(file.Flags, file.Segments));
                }
            }
            var joinedDetails = string.Join("; ", details);
            if (merged.Flags.Count == 0 && merged.Segments.Count == 0)
            {
                _log.Info("Flag overrides: none in effect ({0})", joinedDetails);
                return;
            }
            _log.Info("Flag overrides in effect: {0} ({1})", CountsText(merged.Flags.Count, merged.Segments.Count),
                joinedDetails);
        }

        // Formats flag and segment counts, for example "2 flags, 1 segment".
        internal static string CountsText(int flags, int segments)
        {
            var parts = new List<string>(2);
            if (flags > 0)
            {
                parts.Add(Pluralize(flags, "flag"));
            }
            if (segments > 0)
            {
                parts.Add(Pluralize(segments, "segment"));
            }
            return string.Join(", ", parts);
        }

        private static string Pluralize(int count, string noun) =>
            count == 1 ? "1 " + noun : count + " " + noun + "s";
    }
}
