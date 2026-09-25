namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Integration between the LaunchDarkly SDK and flag overrides read from local files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flag overrides are currently experimental and subject to change.
    /// </para>
    /// <para>
    /// Overrides are flag and segment definitions that take precedence over data received from
    /// LaunchDarkly at evaluation time, on a per-key basis. They exist for resilience during an
    /// incident. An operator can force one or more flags to a known state on a running application,
    /// whether or not the application can reach LaunchDarkly. The override stays in effect until the
    /// operator removes it. Flags not present in the override data are completely unaffected.
    /// </para>
    /// <para>
    /// The override source is an option of the FDv2 data system. Configure it with
    /// <see cref="DataSystemBuilder.Overrides(Subsystems.IComponentConfigurer{Subsystems.IOverrideSource})"/>:
    /// </para>
    /// <code>
    ///     var config = Configuration.Builder("sdk-key")
    ///         .DataSystem(Components.DataSystem().Default()
    ///             .Overrides(FileOverrides.Source().FilePaths("/etc/launchdarkly/overrides.json")))
    ///         .Build();
    /// </code>
    /// <para>
    /// An evaluation that an override affects is marked. The marking is direct or transitive: it
    /// applies when the evaluated flag, a prerequisite at any depth, or a segment read during the
    /// evaluation came from the override files. <see cref="EvaluationReason.OverrideAffected"/> reports
    /// the marking. Marked evaluations appear in analytics summary events only, under separate counters,
    /// so LaunchDarkly can distinguish them. They produce no individual evaluation events.
    /// </para>
    /// </remarks>
    /// <seealso cref="FileOverrideSourceBuilder"/>
    public static class FileOverrides
    {
        /// <summary>
        /// Creates a builder for a file-based override source.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The source reads flag and segment overrides from one or more local files and reloads them as the
        /// files change. The files use the same document format as <see cref="FileData"/>: a JSON object with
        /// optional <c>flags</c>, <c>flagValues</c>, and <c>segments</c> properties. A <c>flagValues</c>
        /// entry is expanded into a full flag definition that is off and serves the given value for every
        /// context. YAML files are supported when a parser is supplied with
        /// <see cref="FileOverrideSourceBuilder.Parser(System.Func{string, object})"/>.
        /// </para>
        /// <para>
        /// When multiple files are configured, their entries are combined. The configured order determines
        /// which file wins under the duplicate keys handling. A reload replaces the entire set of overrides,
        /// so removing an entry from the files removes the override. A configured file that does not exist
        /// contributes no overrides: the file can be created later, deleting it removes its overrides, and
        /// deleting every file removes them all. A file that exists but cannot be read or parsed makes that
        /// whole reload fail. The previously loaded overrides stay in effect, the source logs the failure,
        /// retries after a short delay, and recovers on its own once the file is readable again.
        /// </para>
        /// <para>
        /// Whenever the set of overrides in effect changes, including at startup, the source logs the
        /// overrides in effect and what each configured file supplied, at Info level.
        /// </para>
        /// <para>
        /// By default the source polls the files for changes once per second. See
        /// <see cref="FileOverrideSourceBuilder.ChangeDetection(FileOverrideTypes.ChangeDetection)"/> and
        /// <see cref="FileOverrideSourceBuilder.PollInterval(System.TimeSpan)"/>.
        /// </para>
        /// </remarks>
        /// <returns>a <see cref="FileOverrideSourceBuilder"/></returns>
        public static FileOverrideSourceBuilder Source() => new FileOverrideSourceBuilder();
    }
}
