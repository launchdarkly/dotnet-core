namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// The type of credential used for the environment.
    /// </summary>
    public enum CredentialType
    {
        /// <summary>
        /// A mobile key credential.
        /// </summary>
        MobileKey,
        /// <summary>
        /// An SDK key credential.
        /// </summary>
        SdkKey
    }
}