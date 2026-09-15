namespace LaunchDarkly.Sdk.Internal.Http
{
    /// <summary>
    /// Describes whether a failure is one that can be expected to clear on its own.
    /// </summary>
    /// <remarks>
    /// This is a property of the failure itself, not an instruction about what to do in response.
    /// What a component does with the distinction is that component's own concern.
    /// </remarks>
    public enum FailureClass
    {
        /// <summary>
        /// A failure that can be expected to clear without anyone intervening, such as a 503, a
        /// timeout, or a dropped connection.
        /// </summary>
        Normal,

        /// <summary>
        /// A failure that reflects a condition unlikely to change on its own, such as a rejected
        /// SDK key or an untrusted certificate. Clearing it normally requires someone to change a
        /// configuration value, a credential, or a certificate store.
        /// </summary>
        Unexpected
    }
}
