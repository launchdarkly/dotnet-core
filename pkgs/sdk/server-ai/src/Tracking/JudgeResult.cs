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
    /// Error message when the evaluation failed. Present only when <see cref="Success"/> is <c>false</c>.
    /// </summary>
    public readonly string ErrorMessage;

    /// <summary>
    /// The judge's reasoning behind the score, if provided by the runner.
    /// </summary>
    public readonly string Reasoning;

    /// <summary>
    /// Constructs a <see cref="JudgeResult"/>.
    /// </summary>
    /// <param name="metricKey">the LaunchDarkly metric key; optional per spec</param>
    /// <param name="score">the numeric score; optional per spec</param>
    /// <param name="sampled">whether sampled; defaults to <c>false</c></param>
    /// <param name="success">whether successful; defaults to <c>false</c></param>
    /// <param name="judgeConfigKey">optional judge config key</param>
    /// <param name="errorMessage">optional error message when success is false</param>
    /// <param name="reasoning">optional reasoning behind the score</param>
    public JudgeResult(
        string metricKey = null,
        double score = 0.0,
        bool sampled = false,
        bool success = false,
        string judgeConfigKey = null,
        string errorMessage = null,
        string reasoning = null)
    {
        MetricKey = metricKey;
        Score = score;
        Sampled = sampled;
        Success = success;
        JudgeConfigKey = judgeConfigKey;
        ErrorMessage = errorMessage;
        Reasoning = reasoning;
    }
}
