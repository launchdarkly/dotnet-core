namespace LaunchDarkly.Sdk.Server.Ai.Metrics;

/// <summary>
/// Represents statistics returned by a model provider.
/// </summary>
/// <param name="LatencyMs">the duration of the request</param>
public record struct Statistics(int? LatencyMs);


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
/// <param name="Statistics">the statistics relevant to the request</param>
public record struct ProviderResponse(Usage? Usage, Statistics? Statistics);
