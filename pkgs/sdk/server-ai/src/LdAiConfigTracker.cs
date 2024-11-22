using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// A tracker capable of reporting events related to a particular AI configuration.
/// </summary>
public class LdAiConfigTracker : ILdAiConfigTracker
{
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


    /// <inheritdoc/>
    public LdAiConfig Config { get; }

    /// <inheritdoc/>
    public void TrackDuration(float durationMs) =>
        _client.Track(Duration, _context, _trackData, durationMs);


    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void TrackSuccess()
    {
        _client.Track(Generation, _context, _trackData, 1);
    }

    /// <inheritdoc/>
    public async Task<Response> TrackRequest(Task<Response> request)
    {
        var (result, durationMs) = await MeasureDurationOfTaskMs(request);
        TrackSuccess();

        TrackDuration(result.Metrics?.LatencyMs ?? durationMs);

        if (result.Usage != null)
        {
            TrackTokens(result.Usage.Value);
        }

        return result;
    }

    /// <inheritdoc/>
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
}
