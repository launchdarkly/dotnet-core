namespace LaunchDarkly.Sdk.Server.Ai.Metrics;


/// <summary>
///
/// </summary>
/// <param name="LatencyMs"></param>
public record struct Statistics(int? LatencyMs);


/// <summary>
///
/// </summary>
/// <param name="Total"></param>
/// <param name="Input"></param>
/// <param name="Output"></param>
public record struct Usage(int? Total, int? Input, int? Output);


/// <summary>
///
/// </summary>
/// <param name="Usage"></param>
/// <param name="Statistics"></param>
public record struct ProviderResponse(Usage? Usage, Statistics? Statistics);
