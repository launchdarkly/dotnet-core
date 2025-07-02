namespace LaunchDarkly.Sdk.Integrations.Plugins
{
    /// <summary>
    /// Metadata about the SDK itself.
    /// </summary>
    public sealed class SdkMetadata
    {
        /// <summary>
        /// Gets the id of the SDK. This should match the identifier in the SDK. 
        /// This field should be either the x-launchdarkly-user-agent or the user-agent.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the version of the SDK.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Gets the wrapper name if this SDK is a wrapper.
        /// </summary>
        public string WrapperName { get; }

        /// <summary>
        /// Gets the wrapper version if this SDK is a wrapper.
        /// </summary>
        public string WrapperVersion { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SdkMetadata"/> class.
        /// </summary>
        /// <param name="name">The id of the SDK. This should match the identifier in the SDK. It should be either the x-launchdarkly-user-agent or the user-agent.</param>
        /// <param name="version">The version of the SDK.</param>
        /// <param name="wrapperName">If this SDK is a wrapper, then this should be the wrapper name.</param>
        /// <param name="wrapperVersion">If this SDK is a wrapper, then this should be the wrapper version.</param>
        public SdkMetadata(string name, string version, string wrapperName = null, string wrapperVersion = null)
        {
            Name = name;
            Version = version;
            WrapperName = wrapperName;
            WrapperVersion = wrapperVersion;
        }
    }
} 