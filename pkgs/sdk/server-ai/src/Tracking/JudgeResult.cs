namespace LaunchDarkly.Sdk.Server.Ai.Tracking;

/// <summary>
/// Represents the result of a judge evaluation for use with
/// <c>ILdAiConfigTracker.TrackJudgeResult</c>.
/// </summary>
public sealed record JudgeResult
{
    /// <summary>
    /// The LaunchDarkly metric key to emit the event under.
    /// </summary>
    public readonly string MetricKey;

    /// <summary>
    /// The numeric score for this evaluation.
    /// </summary>
    public readonly double Score;

    /// <summary>
    /// Whether this result was sampled. When <c>false</c>, the event is silently dropped.
    /// </summary>
    public readonly bool Sampled;

    /// <summary>
    /// Whether the judge evaluation succeeded. When <c>false</c>, the event is silently dropped.
    /// </summary>
    public readonly bool Success;

    /// <summary>
    /// Optional AI Judge Config key to include in the event data.
    /// </summary>
    public readonly string JudgeConfigKey;

    /// <summary>
    /// Constructs a <see cref="JudgeResult"/>.
    /// </summary>
    /// <param name="metricKey">the LaunchDarkly metric key</param>
    /// <param name="score">the numeric score</param>
    /// <param name="sampled">whether sampled; defaults to <c>true</c></param>
    /// <param name="success">whether successful; defaults to <c>true</c></param>
    /// <param name="judgeConfigKey">optional judge config key</param>
    public JudgeResult(
        string metricKey,
        double score,
        bool sampled = true,
        bool success = true,
        string judgeConfigKey = null)
    {
        MetricKey = metricKey;
        Score = score;
        Sampled = sampled;
        Success = success;
        JudgeConfigKey = judgeConfigKey;
    }
}
