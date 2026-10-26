using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Internal;
using LaunchDarkly.Sdk.Server.Internal.DataSources;
using LaunchDarkly.Sdk.Server.Internal.FileLoading;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// A builder for configuring the file data source.
    /// </summary>
    /// <remarks>
    /// To use the file data source, obtain a new instance of this class with <see cref="FileData.DataSource"/>;
    /// call the builder method {@link #filePaths(String...)} to specify file path(s), and/or
    /// {@link #classpathResources(String...)} to specify classpath data resources; then pass the resulting
    /// object to <see cref="ConfigurationBuilder.DataSource"/>.
    /// </remarks>
    /// <seealso cref="FileData"/>
    public sealed class FileDataSourceBuilder : IComponentConfigurer<IDataSource>
    {
        internal readonly List<string> _paths = new List<string>();
        internal bool _autoUpdate = false;
        internal FileDataTypes.IFileReader _fileReader = FlagFileReader.Instance;
        internal readonly Dictionary<string, Func<string, object>> _parsers = new Dictionary<string, Func<string, object>>();
        internal bool _skipMissingPaths = false;
        internal FileDataTypes.DuplicateKeysHandling _duplicateKeysHandling = FileDataTypes.DuplicateKeysHandling.Throw;

        /// <summary>
        /// Adds any number of source files for loading flag data, specifying each file path as a string.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The files will not actually be loaded until the LaunchDarkly client starts up.
        /// </para>
        /// <para>
        /// Files are normally expected to contain JSON; see <see cref="Parser(string, Func{string, object})"/> for alternatives.
        /// </para>
        /// </remarks>
        /// <param name="paths">path(s) to the source file(s); may be absolute or relative to the current working directory</param>
        /// <returns>the same factory object</returns>
        public FileDataSourceBuilder FilePaths(params string[] paths)
        {
            _paths.AddRange(paths);
            return this;
        }

        /// <summary>
        /// Registers a parsing function for data files with the given extension, for a format other
        /// than JSON such as YAML.
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
        /// configuration method tells the parser to read a property like <c>on: true</c> or
        /// <c>variation: 3</c> as a boolean or integer rather than a string; YAML syntax is flexible, and
        /// data types matter in flag definitions.
        /// </para>
        /// <code>
        ///     var yaml = new DeserializerBuilder()
        ///         .WithAttemptingUnquotedStringTypeDeserialization()
        ///         .Build();
        ///     var source = FileData.DataSource()
        ///         .FilePaths("./testData/flags.yaml")
        ///         .Parser(".yaml", s => yaml.Deserialize&lt;object&gt;(s));
        /// </code>
        /// </remarks>
        /// <param name="extension">the file extension, with or without the leading period</param>
        /// <param name="parseFn">the parsing function</param>
        /// <returns>the same factory object</returns>
        /// <exception cref="ArgumentException">the extension is null or empty</exception>
        /// <exception cref="ArgumentNullException">the parsing function is null</exception>
        public FileDataSourceBuilder Parser(string extension, Func<string, object> parseFn)
        {
            if (parseFn is null)
            {
                throw new ArgumentNullException(nameof(parseFn));
            }
            _parsers[FileDataParser.NormalizeExtension(extension)] = parseFn;
            return this;
        }

        /// <summary>
        /// Specifies an alternate file reader which can support custom OS error handling and retry logic.
        /// </summary>
        /// <param name="fileReader">The flag file reader.</param>
        /// <returns>the same factory object</returns>
        public FileDataSourceBuilder FileReader(FileDataTypes.IFileReader fileReader)
        {
            _fileReader = fileReader;
            return this;
        }

        /// <summary>
        /// Specifies whether the data source should watch for changes to the source file(s) and reload flags
        /// whenever there is a change.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This option is off by default: unless you set this option to <c>true</c>, files will only be loaded once.
        /// </para>
        /// <para>
        /// Whenever possible, you should update a file's entire contents in one atomic operation; in Unix-like OSes,
        /// that can be done by creating a temporary file, writing to it, and then renaming it to replace the original
        /// file. In Windows, that is not always possible, so FileDataSource might detect an update before the file has
        /// been fully written; in that case it will retry several times until the file can be parsed, giving up after
        /// repeated failures until another change to the file is detected.
        /// </para>
        /// <para>
        /// The data source watches the directory of each file, so a file that is created, replaced, or deleted
        /// is detected, and a directory that does not exist yet is watched once it appears. While a load fails
        /// for any reason, the previously loaded flags stay in effect.
        /// </para>
        /// </remarks>
        /// <param name="autoUpdate">true if flags should be reloaded whenever a source file changes</param>
        /// <returns>the same factory object</returns>
        public FileDataSourceBuilder AutoUpdate(bool autoUpdate)
        {
            _autoUpdate = autoUpdate;
            return this;
        }

        /// <summary>
        /// Specifies how to handle keys that are duplicated across files.
        /// </summary>
        /// <remarks>
        /// <para>
        /// By default, the file data source will throw if keys are duplicated across files.
        /// </para>
        /// </remarks>
        /// <param name="duplicateKeysHandling">how duplicate keys should be handled</param>
        /// <returns>the same factory object</returns>
        public FileDataSourceBuilder DuplicateKeysHandling(FileDataTypes.DuplicateKeysHandling duplicateKeysHandling)
        {
            _duplicateKeysHandling = duplicateKeysHandling;
            return this;
        }

        /// <summary>
        /// Specifies to ignore missing file paths instead of treating them as an error.
        /// </summary>
        /// <param name="skipMissingPaths">If <c>true</c>, missing file paths will be skipped,
        /// otherwise they will be treated as an error</param>
        /// <returns>the same factory object</returns>
        public FileDataSourceBuilder SkipMissingPaths(bool skipMissingPaths)
        {
            _skipMissingPaths = skipMissingPaths;
            return this;
        }

        /// <summary>
        /// Called internally by the SDK to create the data source.
        /// </summary>
        /// <param name="context">the client context</param>
        /// <returns>the data source</returns>
        /// <exception cref="ArgumentException">a file with a YAML extension has no registered parser</exception>
        public IDataSource Build(LdClientContext context)
        {
            // The SDK does not include a YAML parser, so a YAML file without a registered parser can
            // never load. That is a configuration error, reported now rather than on every load.
            var yamlPathWithoutParser = FileDataParser.FindYamlPathWithoutParser(_paths, _parsers);
            if (yamlPathWithoutParser != null)
            {
                throw new ArgumentException("no parser is registered for the YAML data file " + yamlPathWithoutParser +
                    "; register one with Parser(\"" + FileDataParser.ExtensionOf(yamlPathWithoutParser) + "\", ...)");
            }
            return new FileDataSource(context.DataSourceUpdates, _fileReader, _paths, _autoUpdate,
                new Dictionary<string, Func<string, object>>(_parsers), _skipMissingPaths, _duplicateKeysHandling,
                context.Logger.SubLogger(LogNames.DataSourceSubLog));
        }
    }
}
