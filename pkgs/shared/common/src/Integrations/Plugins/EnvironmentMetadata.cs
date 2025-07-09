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
        public string Credential { get; }

        /// <summary>
        /// Gets the type of credential used (e.g., Mobile Key or SDK Key).
        /// </summary>
        public CredentialType CredentialType { get; }

        /// <summary>
        /// Gets the application metadata.
        /// </summary>
        public ApplicationMetadata Application { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EnvironmentMetadata"/> class.
        /// </summary>
        /// <param name="sdkMetadata">the SDK metadata</param>
        /// <param name="credential">the SDK Key or Mobile Key</param>
        /// <param name="credentialType">the type of credential</param>
        /// <param name="applicationMetadata">the application metadata</param>
        public EnvironmentMetadata(SdkMetadata sdkMetadata, string credential, CredentialType credentialType, ApplicationMetadata applicationMetadata)
        {
            Sdk = sdkMetadata;
            Credential = credential;
            CredentialType = credentialType;
            Application = applicationMetadata;
        }
    }
}