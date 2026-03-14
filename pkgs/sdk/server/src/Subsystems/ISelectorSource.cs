namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// Interface for a selector source. Selectors are used to identify versioned payloads.
    /// </summary>
    public interface ISelectorSource
    {
        /// <summary>
        /// Get the current selector.
        /// </summary>
        /// <returns>the current selector</returns>
        Selector Selector { get; }
    }
}
