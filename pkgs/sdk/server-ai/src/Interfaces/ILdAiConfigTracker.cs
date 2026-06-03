using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Records metrics for a single AI run.
/// </summary>
/// <remarks>
/// All events emitted by a tracker share a runId (a UUIDv4) so LaunchDarkly can correlate
/// them. See individual track methods for their specific semantics.
/// Call <c>CreateTracker</c> on the AI Config to start a new run. A
/// <see cref="ResumptionToken"/> preserves the runId, so events emitted by a tracker
/// reconstructed in another process correlate with the original tracker's runId.
/// </remarks>
public interface ILdAiConfigTracker
{
    /// <summary>
    /// A URL-safe Base64-encoded token that can be used to reconstruct this tracker in a different
    /// process or at a later time. The token contains the runId, configKey, variationKey, and version.
    ///
    /// Use <c>LdAiConfigTracker.FromResumptionToken</c> to reconstruct a tracker from this token.
    /// </summary>
    public string ResumptionToken { get; }

    /// <summary>
    /// A summary of the metrics tracked by this tracker.
    /// </summary>
    public MetricSummary Summary { get; }

    /// <summary>
    /// Tracks a duration metric related to this config. For example, if a particular operation
    /// related to usage of the AI model takes 100ms, this can be tracked and made available in
    /// LaunchDarkly.
    /// </summary>
    /// <remarks>Records at most once per Tracker; further calls are ignored.</remarks>
    /// <param name="durationMs">the duration in milliseconds</param>
    public void TrackDuration(float durationMs);

    /// <summary>
    /// Tracks the duration of a task, and returns the result of the task.
    ///
    /// If the provided task throws, then this method will also throw.
    ///
    /// In the case the provided function throws, this function will still
    /// record the duration.
    /// </summary>
    /// <param name="task">the task</param>
    /// <typeparam name="T">type of the task's result</typeparam>
    /// <returns>the task</returns>
    public Task<T> TrackDurationOfTask<T>(Task<T> task);

    /// <summary>
    /// Tracks the time it takes for the first token to be generated.
    /// </summary>
    /// <remarks>Records at most once per Tracker; further calls are ignored.</remarks>
    /// <param name="timeToFirstTokenMs">the duration in milliseconds</param>
    public void TrackTimeToFirstToken(float timeToFirstTokenMs);

    /// <summary>
    /// Tracks feedback (positive or negative) related to the output of the model.
    /// </summary>
    /// <remarks>Records at most once per Tracker; further calls are ignored.</remarks>
    /// <param name="feedback">the feedback</param>
    /// <exception cref="ArgumentOutOfRangeException">thrown if the feedback value is not <see cref="Feedback.Positive"/> or <see cref="Feedback.Negative"/></exception>
    public void TrackFeedback(Feedback feedback);

    /// <summary>
    /// Tracks a generation event related to this config.
    /// </summary>
    /// <remarks>
    /// Records at most once per Tracker. TrackSuccess and TrackError share state; only
    /// one of the two can record per Tracker, and subsequent calls are ignored.
    /// </remarks>
    public void TrackSuccess();

    /// <summary>
    /// Tracks an unsuccessful generation event related to this config.
    /// </summary>
    /// <remarks>
    /// Records at most once per Tracker. TrackSuccess and TrackError share state; only
    /// one of the two can record per Tracker, and subsequent calls are ignored.
    /// </remarks>
    public void TrackError();

    /// <summary>
    /// Tracks a request to a provider. The request is a task that returns a <see cref="Response"/>, which
    /// contains information about the request such as token usage and metrics.
    ///
    /// This function will track the duration of the operation, the token
    /// usage, and the success or error status.
    ///
    /// If the provided function throws, then this method will also throw.
    ///
    /// In the case the provided function throws, this function will record the
    /// duration and an error.
    ///
    /// A failed operation will not have any token usage data.
    ///
    /// It is the responsibility of the caller to fill in the <see cref="Response"/> object with any details
    /// that should be tracked.
    ///
    /// Example:
    /// <code>
    /// var response = tracker.TrackRequest(Task.Run(() => new Response {
    ///     // 1. Make a request to the AI provider
    ///     // 2. Identify relevant statistics returned in the response
    ///     // 3. Return a Response object containing the relevant statistics
    ///     Usage = new Usage { Total = 1, Input = 1, Output = 1 },
    ///     Metrics = new Metrics { LatencyMs = 100 }
    /// }));
    /// </code>
    ///
    /// If no latency statistic is explicitly returned in the <see cref="Response"/>, then the duration of the
    /// Task is automatically measured and recorded as the latency metric associated with this request.
    ///
    /// </summary>
    /// <remarks>
    /// Subsequent calls re-run the task but emit only metrics not already recorded on this Tracker.
    /// Call <c>CreateTracker</c> on the AI Config to start a new run.
    /// </remarks>
    /// <param name="request">a task representing the request</param>
    /// <returns>the task</returns>
    public Task<Response> TrackRequest(Task<Response> request);

    /// <summary>
    /// Tracks token usage related to this config.
    /// </summary>
    /// <remarks>Records at most once per Tracker; further calls are ignored.</remarks>
    /// <param name="usage">the token usage</param>
    public void TrackTokens(Usage usage);
}
