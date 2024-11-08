namespace LaunchDarkly.Sdk.Server.Ai.Provider;

/// <summary>
/// Feedback about the generated content.
/// </summary>
public enum Feedback
{
    /// <summary>
    /// The sentiment was positive.
    /// </summary>
    Positive,

    /// <summary>
    /// The sentiment was negative.
    /// </summary>
    Negative,
}
