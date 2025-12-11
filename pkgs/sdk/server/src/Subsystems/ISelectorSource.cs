namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Interface for a selector source. Selectors are used to identify versioned payloads.
    /// </summary>
    internal interface ISelectorSource
    {
        // TODO: SDK-1678: Internal until ready for use.

        /// <summary>
        /// Get the current selector.
        /// </summary>
        /// <returns>the current selector</returns>
        Selector Selector { get; }
    }
}
