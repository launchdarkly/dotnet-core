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
        /// entry is expanded into a full flag definition that is on and serves the given value as its
        /// fallthrough for every context. Such a flag is not available to client-side SDKs, so
        /// <see cref="LdClient.AllFlagsState"/> with <see cref="FlagsStateOption.ClientSideOnly"/> leaves it
        /// out, even when the flag it overrides is available to them. To override such a flag, define it
        /// in <c>flags</c> with <c>"clientSide": true</c>. A flag or segment is listed under its key: a <c>key</c> property
        /// inside the definition is optional and must be equal to it, and a <c>version</c> property is
        /// optional. The parser for a file is chosen by the file's extension. A file whose extension has a
        /// parser registered with <see cref="FileOverrideSourceBuilder.Parser(string, System.Func{string, object})"/>,
        /// for example a YAML parser for <c>.yaml</c>, is read with that parser, and every other file is
        /// read as JSON.
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
        /// A file can be rewritten in place. To remove a file's overrides, delete it or write a document with
        /// no entries, such as <c>{}</c>. A JSON file with no content is not a valid document: it fails to
        /// load, and the overrides in effect stay. A file whose content is in effect and that is found
        /// empty, or that its parser reads as having no entries, such as YAML with only a comment, may be
        /// in the middle of a rewrite. The source does not apply it until it has stayed so, without being
        /// written, for about a second, and changes to the other files wait for it, since a reload applies
        /// all the files or none. An emptied YAML file therefore removes its overrides about a second
        /// later, or at most about five seconds later while files keep being rewritten. A writer that leaves
        /// a YAML file empty for longer than a second while it rewrites it briefly removes the file's
        /// overrides. YAML that is cut off between two entries can still parse, so for a YAML file,
        /// replacing its entire contents in one operation, such as renaming a fully written temporary file
        /// over it, avoids briefly applying only a part of it.
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
