namespace LaunchDarkly.Sdk.Server.Ai.Tracking;

/// <summary>
/// Holds the metrics extracted from an AI operation for use with
/// <c>ILdAiConfigTracker.TrackMetricsOf</c>.
/// </summary>
public sealed record AiMetrics
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public readonly bool Success;

    /// <summary>
    /// Optional token usage for the operation.
    /// </summary>
    public readonly Usage? Tokens;

    /// <summary>
    /// Constructs an <see cref="AiMetrics"/> value.
    /// </summary>
    /// <param name="success">whether the operation succeeded</param>
    /// <param name="tokens">optional token usage</param>
    public AiMetrics(bool success, Usage? tokens = null)
    {
        Success = success;
        Tokens = tokens;
    }
}
