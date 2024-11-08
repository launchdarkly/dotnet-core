namespace LaunchDarkly.Sdk.Server.Ai.Provider;

/// <summary>
/// Represents metrics returned by a model provider.
/// </summary>
/// <param name="LatencyMs">the duration of the request in milliseconds</param>
public record struct Metrics(long? LatencyMs);


/// <summary>
/// Represents token usage.
/// </summary>
/// <param name="Total">the total tokens used</param>
/// <param name="Input">the tokens sent as input</param>
/// <param name="Output">the tokens received as output</param>
public record struct Usage(int? Total, int? Input, int? Output);


/// <summary>
/// Represents information returned by a model provider.
/// </summary>
/// <param name="Usage">the token usage</param>
/// <param name="Metrics">the metrics relevant to the request</param>
public record struct Response(Usage? Usage, Metrics? Metrics);
