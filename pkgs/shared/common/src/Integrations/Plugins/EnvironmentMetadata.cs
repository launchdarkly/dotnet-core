namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    /// <summary>
    /// Metadata about the environment where the SDK is running.
    /// </summary>
    public sealed class EnvironmentMetadata
    {
        /// <summary>
        /// Gets the SDK metadata.
        /// </summary>
        public SdkMetadata Sdk { get; }

        /// <summary>
        /// Gets the SDK key.
        /// </summary>
        public string SdkKey { get; }

        /// <summary>
        /// Gets the application metadata.
        /// </summary>
        public ApplicationMetadata Application { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EnvironmentMetadata"/> class.
        /// </summary>
        /// <param name="sdkMetadata">the SDK metadata</param>
        /// <param name="sdkKey">the SDK key</param>
        /// <param name="applicationMetadata">the application metadata</param>
        public EnvironmentMetadata(SdkMetadata sdkMetadata, string sdkKey, ApplicationMetadata applicationMetadata)
        {
            Sdk = sdkMetadata;
            SdkKey = sdkKey;
            Application = applicationMetadata;
        }
    }
} 