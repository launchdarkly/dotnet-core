using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Tracking;

/// <summary>
/// A summary of the metrics tracked by an <c>AiGraphTracker</c> during a graph invocation.
/// </summary>
/// <param name="Success">whether the overall graph invocation succeeded</param>
/// <param name="DurationMs">the total duration in milliseconds</param>
/// <param name="Tokens">the aggregate token usage across the graph</param>
/// <param name="Path">the sequence of node keys visited during execution</param>
/// <param name="NodeMetrics">per-node metric summaries keyed by agent config key</param>
/// <param name="ResumptionToken">the resumption token for cross-process continuation</param>
public record struct AiGraphMetricSummary(
    bool? Success,
    double? DurationMs,
    Usage? Tokens,
    IReadOnlyList<string> Path,
    IReadOnlyDictionary<string, MetricSummary> NodeMetrics,
    string ResumptionToken
);
