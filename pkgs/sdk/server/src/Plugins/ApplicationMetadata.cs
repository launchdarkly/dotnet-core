namespace LaunchDarkly.Sdk.Server.Plugins
{
    /// <summary>
    /// </summary>
    public sealed class ApplicationMetadata
    {
        /// <summary>
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// </summary>
        public ApplicationMetadata(string id = null, string version = null)
        {
            Id = id;
            Version = version;
        }
    }
}
