using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LaunchDarkly.Sdk.Server.Internal;
using LaunchDarkly.Sdk.Server.Internal.FileLoading;
using LaunchDarkly.Sdk.Server.Internal.Overrides;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// A builder for configuring the file-based override source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Obtain an instance with <see cref="FileOverrides.Source"/>, call <see cref="FilePaths(string[])"/>
    /// to specify the override file(s), and pass the builder to
    /// <see cref="DataSystemBuilder.Overrides(IComponentConfigurer{IOverrideSource})"/>.
    /// </para>
    /// <para>
    /// Flag overrides are currently experimental and subject to change.
    /// </para>
    /// </remarks>
    /// <seealso cref="FileOverrides"/>
    public sealed class FileOverrideSourceBuilder : IComponentConfigurer<IOverrideSource>
    {
        /// <summary>
        /// The interval at which the source examines the files for changes in polling mode when no interval
        /// was specified: one second. The source reads local files rather than contacting a service, so a
        /// short interval keeps an override responsive during an incident at negligible cost.
        /// </summary>
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The shortest allowed polling interval: one second. A configured interval below this is raised to
        /// it. The minimum exists only to prevent a tight loop over the file system.
        /// </summary>
        public static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The longest polling interval the timer supports, about 49 days. A configured interval above
        /// it is a configuration error.
        /// </summary>
        internal static readonly TimeSpan MaximumPollInterval = TimeSpan.FromMilliseconds(4294967294);

        private static readonly StringComparer PathComparer =
            Environment.OSVersion.Platform == PlatformID.Win32NT ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        internal readonly List<string> _paths = new List<string>();
        internal FileOverrideTypes.DuplicateKeysHandling _duplicateKeysHandling = FileOverrideTypes.DuplicateKeysHandling.Fail;
        internal FileOverrideTypes.ChangeDetection _changeDetection = FileOverrideTypes.ChangeDetection.Polling;
        internal TimeSpan _pollInterval = DefaultPollInterval;
        internal readonly Dictionary<string, Func<string, object>> _parsers = new Dictionary<string, Func<string, object>>();

        internal FileOverrideSourceBuilder() { }

        /// <summary>
        /// Adds one or more override files, specifying each file path as a string.
        /// </summary>
        /// <remarks>
        /// The order is significant. It determines which file wins under the duplicate keys handling when
        /// the same key appears in more than one file. A relative path is resolved against the current
        /// working directory when the client is created. A configured file does not need to exist.
        /// </remarks>
        /// <param name="paths">path(s) to the override file(s)</param>
        /// <returns>the same builder</returns>
        public FileOverrideSourceBuilder FilePaths(params string[] paths)
        {
            _paths.AddRange(paths);
            return this;
        }

        /// <summary>
        /// Specifies how to handle the same key appearing in more than one file.
        /// </summary>
        /// <remarks>
        /// The default is <see cref="FileOverrideTypes.DuplicateKeysHandling.Fail"/>. A key defined in two
        /// files is most likely a mistake. During an incident, a failed reload that leaves the last good
        /// overrides in place and logs the conflict is safer than silently serving one of the two definitions.
        /// </remarks>
        /// <param name="duplicateKeysHandling">how duplicate keys should be handled</param>
        /// <returns>the same builder</returns>
        public FileOverrideSourceBuilder DuplicateKeysHandling(FileOverrideTypes.DuplicateKeysHandling duplicateKeysHandling)
        {
            _duplicateKeysHandling = duplicateKeysHandling;
            return this;
        }

        /// <summary>
        /// Selects how the source detects file changes.
        /// </summary>
        /// <remarks>
        /// The default is <see cref="FileOverrideTypes.ChangeDetection.Polling"/>. The two modes are
        /// alternatives, so setting one replaces the other. Change detection is always on: an override
        /// source that needs a restart to pick up a change would defeat its purpose.
        /// </remarks>
        /// <param name="changeDetection">the change detection mode</param>
        /// <returns>the same builder</returns>
        public FileOverrideSourceBuilder ChangeDetection(FileOverrideTypes.ChangeDetection changeDetection)
        {
            _changeDetection = changeDetection;
            return this;
        }

        /// <summary>
        /// Sets the interval between examinations of the files in polling mode. Watching mode ignores it.
        /// </summary>
        /// <remarks>
        /// The default is <see cref="DefaultPollInterval"/>. An interval below <see cref="MinimumPollInterval"/>
        /// is raised to the minimum, and a warning is logged when the client is created. An interval
        /// longer than the timer supports, about 49 days, is a configuration error that is reported
        /// when the client is created.
        /// </remarks>
        /// <param name="pollInterval">the polling interval</param>
        /// <returns>the same builder</returns>
        public FileOverrideSourceBuilder PollInterval(TimeSpan pollInterval)
        {
            _pollInterval = pollInterval;
            return this;
        }

        /// <summary>
        /// Registers a parsing function for override files with the given extension, for a format
        /// other than JSON such as YAML.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The parser for a file is chosen by the file's extension: a file whose extension is registered
        /// here is parsed with its function, and every other file is parsed as JSON. The extension is the
        /// part of the file name from its last period, compared without regard to letter case, and the
        /// leading period is optional here. Registering an extension again replaces its function.
        /// </para>
        /// <para>
        /// To avoid bringing in additional dependencies that might conflict with application dependencies,
        /// the SDK does not include a YAML parser, but you can register one with this method. A configured
        /// file whose extension is <c>.yaml</c> or <c>.yml</c> and has no registered parser can never load,
        /// so it is a configuration error that is reported when the client is created.
        /// </para>
        /// <para>
        /// The function takes the file content and returns an <c>object</c> made of the basic types that
        /// can be represented in JSON: <c>string</c>, numbers, booleans, <c>List</c>, and
        /// <c>Dictionary&lt;string, object&gt;</c>. It should throw an exception if it cannot parse the
        /// content. Here is an example with the <c>YamlDotNet</c> package. The <c>DeserializerBuilder</c>
        /// configuration method tells the parser to read a property like <c>on: true</c> as a boolean
        /// rather than a string; data types matter in flag definitions.
        /// </para>
        /// <code>
        ///     var yaml = new DeserializerBuilder()
        ///         .WithAttemptingUnquotedStringTypeDeserialization()
        ///         .Build();
        ///     var source = FileOverrides.Source()
        ///         .FilePaths("/etc/launchdarkly/overrides.yaml")
        ///         .Parser(".yaml", s => yaml.Deserialize&lt;object&gt;(s));
        /// </code>
        /// </remarks>
        /// <param name="extension">the file extension, with or without the leading period</param>
        /// <param name="parseFn">the parsing function</param>
        /// <returns>the same builder</returns>
        /// <exception cref="ArgumentException">the extension is null or empty</exception>
        /// <exception cref="ArgumentNullException">the parsing function is null</exception>
        public FileOverrideSourceBuilder Parser(string extension, Func<string, object> parseFn)
        {
            if (parseFn is null)
            {
                throw new ArgumentNullException(nameof(parseFn));
            }
            _parsers[FileDataParser.NormalizeExtension(extension)] = parseFn;
            return this;
        }

        /// <summary>
        /// Called internally by the SDK to create the override source.
        /// </summary>
        /// <remarks>
        /// Invalid configuration is reported the same way as for other components: an exception is thrown,
        /// and the client is not created. A configured file that does not exist is not a configuration error.
        /// </remarks>
        /// <param name="context">the client context</param>
        /// <returns>the override source</returns>
        /// <exception cref="ArgumentException">no file paths were specified, or a file with a YAML extension
        /// has no registered parser</exception>
        /// <exception cref="ArgumentOutOfRangeException">an option has a value outside its enumeration, or
        /// the poll interval is longer than the timer supports</exception>
        public IOverrideSource Build(LdClientContext context)
        {
            if (_paths.Count == 0)
            {
                throw new ArgumentException("no file paths were specified for the file-based override source");
            }
            var logger = context.Logger.SubLogger(LogNames.OverridesSubLog);

            // A path listed more than once would make every entry of that file a duplicate of itself, so
            // the repeats are dropped and the first occurrence keeps its place in the order. Windows
            // file names do not distinguish letter case, so a repeat in different case is a repeat
            // there; elsewhere the two spellings can name different files.
            var allPaths = _paths.Select(Path.GetFullPath).ToList();
            var paths = allPaths.Distinct(PathComparer).ToList();
            if (paths.Count != allPaths.Count)
            {
                logger.Warn("File paths list the same file more than once; the repeats are ignored");
            }
            // The SDK does not include a YAML parser, so a YAML file without a registered parser can
            // never load. That is a configuration error, reported now rather than on every reload.
            var yamlPathWithoutParser = FileDataParser.FindYamlPathWithoutParser(paths, _parsers);
            if (yamlPathWithoutParser != null)
            {
                throw new ArgumentException("no parser is registered for the YAML override file " + yamlPathWithoutParser +
                    "; register one with Parser(\"" + FileDataParser.ExtensionOf(yamlPathWithoutParser) + "\", ...)");
            }

            switch (_duplicateKeysHandling)
            {
                case FileOverrideTypes.DuplicateKeysHandling.Fail:
                case FileOverrideTypes.DuplicateKeysHandling.Ignore:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(DuplicateKeysHandling), _duplicateKeysHandling,
                        "unrecognized duplicate keys handling for the file-based override source");
            }
            switch (_changeDetection)
            {
                case FileOverrideTypes.ChangeDetection.Polling:
                case FileOverrideTypes.ChangeDetection.Watching:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ChangeDetection), _changeDetection,
                        "unrecognized change detection mode for the file-based override source");
            }

            var pollInterval = _pollInterval;
            if (_changeDetection == FileOverrideTypes.ChangeDetection.Polling)
            {
                if (pollInterval > MaximumPollInterval)
                {
                    throw new ArgumentOutOfRangeException(nameof(PollInterval), pollInterval,
                        "poll interval for the file-based override source must not exceed " + MaximumPollInterval);
                }
                if (pollInterval < MinimumPollInterval)
                {
                    logger.Warn("Poll interval {0} is below the minimum; using {1}", pollInterval, MinimumPollInterval);
                    pollInterval = MinimumPollInterval;
                }
            }

            return new FileOverrideSource(paths, _duplicateKeysHandling, _changeDetection, pollInterval,
                new Dictionary<string, Func<string, object>>(_parsers), logger);
        }
    }
}
