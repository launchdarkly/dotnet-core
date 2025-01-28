using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// A utility capable of generating events related to a specific AI model
/// configuration.
/// </summary>
public interface ILdAiConfigTracker
{
    /// <summary>
    /// The AI model configuration retrieved from LaunchDarkly, or a default value if unable to retrieve.
    /// </summary>
    public LdAiConfig Config { get; }

    /// <summary>
    /// Tracks a duration metric related to this config. For example, if a particular operation
    /// related to usage of the AI model takes 100ms, this can be tracked and made available in
    /// LaunchDarkly.
    /// </summary>
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
    /// <param name="timeToFirstTokenMs">the duration in milliseconds</param>
    public void TrackTimeToFirstToken(float timeToFirstTokenMs);

    /// <summary>
    /// Tracks feedback (positive or negative) related to the output of the model.
    /// </summary>
    /// <param name="feedback">the feedback</param>
    /// <exception cref="ArgumentOutOfRangeException">thrown if the feedback value is not <see cref="Feedback.Positive"/> or <see cref="Feedback.Negative"/></exception>
    public void TrackFeedback(Feedback feedback);

    /// <summary>
    /// Tracks a generation event related to this config.
    /// </summary>
    public void TrackSuccess();

    /// <summary>
    /// Tracks an unsuccessful generation event related to this config.
    /// </summary>
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
    /// <param name="request">a task representing the request</param>
    /// <returns>the task</returns>
    public Task<Response> TrackRequest(Task<Response> request);

    /// <summary>
    /// Tracks token usage related to this config.
    /// </summary>
    /// <param name="usage">the token usage</param>
    public void TrackTokens(Usage usage);
}
