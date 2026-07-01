using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai.Evals;

/// <summary>
/// The result of a model invocation performed by an <see cref="LaunchDarkly.Sdk.Server.Ai.Interfaces.IRunner"/>.
/// </summary>
public sealed record RunnerResult(
    /// <summary>
    /// The response text from the model provider.
    /// </summary>
    string Content,

    /// <summary>
    /// Success and token metrics for the invocation.
    /// </summary>
    AiMetrics Metrics,

    /// <summary>
    /// The unmodified provider response. Optional; intended for advanced callers that need
    /// access to provider-specific fields.
    /// </summary>
    object Raw = null,

    /// <summary>
    /// Structured output parsed from the response when <c>outputType</c> was provided to
    /// <c>RunAsync</c>. <c>null</c> when no output schema was requested.
    /// </summary>
    IReadOnlyDictionary<string, object> Parsed = null
);
