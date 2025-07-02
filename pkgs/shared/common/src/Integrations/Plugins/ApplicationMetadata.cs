namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    /// <summary>
    /// Metadata about the application where the SDK is running.
    /// </summary>
    public sealed class ApplicationMetadata
    {
        /// <summary>
        /// Gets the application identifier.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the application version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the application name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the application version name.
        /// </summary>
        public string VersionName { get; }
                
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationMetadata"/> class with the specified application ID, version, name, and version name.
        /// </summary>
        /// <param name="id">The application identifier.</param>
        /// <param name="version">The application version.</param>
        /// <param name="name">The application name.</param>
        /// <param name="versionName">The application version name.</param>
        public ApplicationMetadata(string id = null, string version = null, string name = null, string versionName = null)
        {
            Id = id;
            Version = version;
            Name = name;
            VersionName = versionName;
        }
    }
} 