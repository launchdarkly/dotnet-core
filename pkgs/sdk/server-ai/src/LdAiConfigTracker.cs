using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Metrics;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// A tracker capable of reporting events related to a particular AI configuration.
/// </summary>
public class LdAiConfigTracker : IDisposable
{
    /// <summary>
    /// The retrieved AI model configuration.
    /// </summary>
    public readonly LdAiConfig Config;


    private readonly ILaunchDarklyClient _client;
    private readonly Context _context;
    private readonly LdValue _trackData;

    private const string Duration = "$ld:ai:duration:total";
    private const string FeedbackPositive = "$ld:ai:feedback:user:positive";
    private const string FeedbackNegative = "$ld:ai:feedback:user:negative";
    private const string Generation = "$ld:ai:generation";
    private const string TokenTotal = "$ld:ai:tokens:total";
    private const string TokenInput = "$ld:ai:tokens:input";
    private const string TokenOutput = "$ld:ai:tokens:output";

    /// <summary>
    /// Constructs a new AI configuration tracker. The tracker is associated with a configuration,
    /// a context, and a key which identifies the configuration.
    /// </summary>
    /// <param name="client">the LaunchDarkly client</param>
    /// <param name="configKey">key of the AI config</param>
    /// <param name="config">the AI config</param>
    /// <param name="context">the context</param>
    /// <exception cref="ArgumentNullException"></exception>
    public LdAiConfigTracker(ILaunchDarklyClient client, string configKey, LdAiConfig config, Context context)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _context = context;
        _trackData =  LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            { "versionKey", LdValue.Of(Config.VersionKey)},
            { "configKey" , LdValue.Of(configKey ?? throw new ArgumentNullException(nameof(configKey))) }
        });
    }

    /// <summary>
    /// Tracks a duration metric related to this config.
    /// </summary>
    /// <param name="durationMs">the duration in milliseconds</param>
    public void TrackDuration(float durationMs) =>
        _client.Track(Duration, _context, _trackData, durationMs);


    /// <summary>
    /// Tracks the duration of a task, and returns the result of the task.
    /// </summary>
    /// <param name="task">the task</param>
    /// <typeparam name="T">type of the task's result</typeparam>
    /// <returns>the task</returns>
    public async Task<T> TrackDurationOfTask<T>(Task<T> task)
    {
        var result = await MeasureDurationOfTaskMs(task);
        TrackDuration(result.Item2);
        return result.Item1;
    }

    private static async Task<Tuple<T, long>> MeasureDurationOfTaskMs<T>(Task<T> task)
    {
        var sw = Stopwatch.StartNew();
        var result = await task;
        sw.Stop();
        return Tuple.Create(result, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Tracks feedback (positive or negative) related to the output of the model.
    /// </summary>
    /// <param name="feedback">the feedback</param>
    /// <exception cref="ArgumentOutOfRangeException">thrown if the feedback value is not <see cref="Feedback.Positive"/> or <see cref="Feedback.Negative"/></exception>
    public void TrackFeedback(Feedback feedback)
    {
        switch (feedback)
        {
            case Feedback.Positive:
                _client.Track(FeedbackPositive, _context, _trackData, 1);
                break;
            case Feedback.Negative:
                _client.Track(FeedbackNegative, _context, _trackData, 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feedback), feedback, null);
        }
    }

    /// <summary>
    /// Tracks a generation event related to this config.
    /// </summary>
    public void TrackSuccess()
    {
        _client.Track(Generation, _context, _trackData, 1);
    }


    /// <summary>
    /// Tracks a request to a provider. The request is a task that returns a <see cref="ProviderResponse"/>, which
    /// contains information about the request such as token usage and statistics.
    /// </summary>
    /// <param name="request">a task representing the request</param>
    /// <returns>the task</returns>
    public async Task<ProviderResponse> TrackRequest(Task<ProviderResponse> request)
    {
        var (result, durationMs) = await MeasureDurationOfTaskMs(request);
        TrackSuccess();

        TrackDuration(result.Statistics?.LatencyMs ?? durationMs);

        if (result.Usage != null)
        {
            TrackTokens(result.Usage.Value);
        }

        return result;
    }

    /// <summary>
    /// Tracks token usage related to this config.
    /// </summary>
    /// <param name="usage">the usage</param>
    public void TrackTokens(Usage usage)
    {
        if (usage.Total is > 0)
        {
            _client.Track(TokenTotal, _context, _trackData, usage.Total.Value);
        }
        if (usage.Input is > 0)
        {
            _client.Track(TokenInput, _context, _trackData, usage.Input.Value);
        }
        if (usage.Output is > 0)
        {
            _client.Track(TokenOutput, _context, _trackData, usage.Output.Value);
        }
    }


    // TODO: Is LdAiClient owning or not?
    /// <summary>
    /// Disposes the client.
    /// </summary>
    public void Dispose() => _client.Dispose();
}
