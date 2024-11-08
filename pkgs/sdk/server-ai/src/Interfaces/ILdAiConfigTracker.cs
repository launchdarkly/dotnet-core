using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Metrics;

namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Represents the interface of the AI Config Tracker, useful for mocking.
/// </summary>
public interface ILdAiConfigTracker
{
    /// <summary>
    /// The retrieved AI model configuration.
    /// </summary>
    public LdAiConfig Config { get; }

    /// <summary>
    /// Tracks a duration metric related to this config.
    /// </summary>
    /// <param name="durationMs">the duration in milliseconds</param>
    public void TrackDuration(float durationMs);

    /// <summary>
    /// Tracks the duration of a task, and returns the result of the task.
    /// </summary>
    /// <param name="task">the task</param>
    /// <typeparam name="T">type of the task's result</typeparam>
    /// <returns>the task</returns>
    public Task<T> TrackDurationOfTask<T>(Task<T> task);

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
    /// Tracks a request to a provider. The request is a task that returns a <see cref="ProviderResponse"/>, which
    /// contains information about the request such as token usage and metrics.
    /// </summary>
    /// <param name="request">a task representing the request</param>
    /// <returns>the task</returns>
    public Task<ProviderResponse> TrackRequest(Task<ProviderResponse> request);

    /// <summary>
    /// Tracks token usage related to this config.
    /// </summary>
    /// <param name="usage">the usage</param>
    public void TrackTokens(Usage usage);
}
