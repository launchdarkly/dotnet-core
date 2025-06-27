namespace LaunchDarkly.Sdk.Server.Plugins
{
    /// <summary>
    /// </summary>
    public sealed class EnvironmentMetadata
    {
        /// <summary>
        /// </summary>
        public SdkMetadata Sdk { get; }

        /// <summary>
        /// </summary>
        public string SdkKey { get; }

        /// <summary>
        /// </summary>
        public ApplicationMetadata Application { get; }

        /// <summary>
        /// </summary>
        /// <param name="sdk">the SDK metadata</param>
        /// <param name="sdkKey">the SDK key</param>
        /// <param name="application">the application metadata</param>
        public EnvironmentMetadata(SdkMetadata sdk, string sdkKey, ApplicationMetadata application)
        {
            Sdk = sdk;
            SdkKey = sdkKey;
            Application = application;
        }
    }
}
