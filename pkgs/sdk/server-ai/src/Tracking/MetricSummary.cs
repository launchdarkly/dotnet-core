namespace LaunchDarkly.Sdk.Server.Ai.Tracking;

/// <summary>
/// A summary of the metrics tracked by a tracker.
/// </summary>
/// <param name="DurationMs">the duration in milliseconds</param>
/// <param name="Feedback">the feedback sentiment</param>
/// <param name="Tokens">the token usage</param>
/// <param name="Success">whether the generation was successful</param>
/// <param name="TimeToFirstTokenMs">the time to first token in milliseconds</param>
public record struct MetricSummary(
    double? DurationMs,
    Feedback? Feedback,
    Usage? Tokens,
    bool? Success,
    double? TimeToFirstTokenMs
);
