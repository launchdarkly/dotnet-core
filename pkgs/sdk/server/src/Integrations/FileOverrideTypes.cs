namespace LaunchDarkly.Sdk.Server.Integrations
{
    /// <summary>
    /// Types that are used in configuring <see cref="FileOverrideSourceBuilder"/>.
    /// </summary>
    /// <remarks>
    /// Flag overrides are currently experimental and subject to change.
    /// </remarks>
    public static class FileOverrideTypes
    {
        /// <summary>
        /// Determines what happens when the same flag or segment key appears in more than one override file.
        /// </summary>
        /// <seealso cref="FileOverrideSourceBuilder.DuplicateKeysHandling(DuplicateKeysHandling)"/>
        public enum DuplicateKeysHandling
        {
            /// <summary>
            /// The reload fails, in the same way as a file that cannot be parsed. The previously loaded
            /// overrides stay in effect. This is the default.
            /// </summary>
            Fail,

            /// <summary>
            /// The entry from the first configured file that defines the key is kept. The others are discarded.
            /// </summary>
            Ignore
        }

        /// <summary>
        /// Selects how the file-based override source learns that a file changed. The two modes are alternatives.
        /// </summary>
        /// <seealso cref="FileOverrideSourceBuilder.ChangeDetection(ChangeDetection)"/>
        public enum ChangeDetection
        {
            /// <summary>
            /// The source examines the files on a fixed interval and reloads when the modification time or
            /// the size of a file changes, or a file appears or disappears. Polling works on every file system,
            /// including network mounts and directories whose contents are swapped through symbolic links, as
            /// Kubernetes does for mounted ConfigMaps. This is the default.
            /// </summary>
            Polling,

            /// <summary>
            /// The source reloads in response to file system change notifications. It reacts faster than
            /// polling. It depends on notifications, which some file systems do not deliver reliably.
            /// </summary>
            Watching
        }
    }
}
