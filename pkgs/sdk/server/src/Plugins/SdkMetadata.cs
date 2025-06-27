namespace LaunchDarkly.Sdk.Server.Plugins
{
    /// <summary>
    /// </summary>
    public sealed class SdkMetadata
    {
        /// <summary>
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// </summary>
        public string WrapperName { get; }

        /// <summary>
        /// </summary>
        public string WrapperVersion { get; }

        /// <summary>
        /// </summary>
        public SdkMetadata(string name, string version, string wrapperName = null, string wrapperVersion = null)
        {
            Name = name;
            Version = version;
            WrapperName = wrapperName;
            WrapperVersion = wrapperVersion;
        }
    }
}
