namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// An object that encapsulates application metadata.
    /// </summary>
    public readonly struct ApplicationInfo
    {
        /// <summary>
        /// A unique identifier representing the application where the LaunchDarkly SDK is running.
        /// </summary>
        public string ApplicationId { get; }

        /// <summary>
        /// A human friendly name for the application in which the LaunchDarkly SDK is running.
        /// </summary>
        public string ApplicationName { get; }

        /// <summary>
        /// A value representing the version of the application where the LaunchDarkly SDK is running.
        /// </summary>
        public string ApplicationVersion { get; }

        /// <summary>
        /// A human friendly name for the version of the application in which the LaunchDarkly SDK is running.
        /// </summary>
        public string ApplicationVersionName { get; }

        /// <summary>
        /// Constructs a new ApplicationInfo instance.
        /// </summary>
        /// <param name="id">id of the application</param>
        /// <param name="name">name of the application</param>
        /// <param name="version">version of the application</param>
        /// <param name="versionName">friendly name for the version</param>
        public ApplicationInfo(string id, string name, string version, string versionName)
        {
            ApplicationId = id;
            ApplicationName = name;
            ApplicationVersion = version;
            ApplicationVersionName = versionName;
        }
    }
}
