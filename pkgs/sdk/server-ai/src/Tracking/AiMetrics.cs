using System.Collections.Generic;

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
    /// Optional list of tool keys invoked during the operation.
    /// </summary>
    public readonly IReadOnlyList<string> ToolCalls;

    /// <summary>
    /// Optional duration override in milliseconds. When set, this value is used instead
    /// of the stopwatch measurement in <c>TrackMetricsOf</c>.
    /// </summary>
    public readonly double? DurationMs;

    /// <summary>
    /// Constructs an <see cref="AiMetrics"/> value.
    /// </summary>
    /// <param name="success">whether the operation succeeded</param>
    /// <param name="tokens">optional token usage</param>
    /// <param name="toolCalls">optional list of tool keys invoked</param>
    /// <param name="durationMs">optional duration override in milliseconds</param>
    public AiMetrics(bool success, Usage? tokens = null,
        IReadOnlyList<string> toolCalls = null, double? durationMs = null)
    {
        Success = success;
        Tokens = tokens;
        ToolCalls = toolCalls;
        DurationMs = durationMs;
    }
}
