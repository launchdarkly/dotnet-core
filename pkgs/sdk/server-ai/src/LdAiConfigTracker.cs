using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Metrics;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// TBD
/// </summary>
public class LdAiConfigTracker : IDisposable
{
    /// <summary>
    /// TBD
    /// </summary>
    public readonly LdAiConfig Config;

    /// <summary>
    /// TBD
    /// </summary>
    private readonly ILaunchDarklyClient _client;

    private readonly Context _context;

    private readonly string _key;

    private readonly LdValue _trackData;

    private const string Duration = "$ld:ai:duration:total";
    private const string FeedbackPositive = "$ld:ai:feedback:user:positive";
    private const string FeedbackNegative = "$ld:ai:feedback:user:negative";
    private const string Generation = "$ld:ai:generation";
    private const string TokenTotal = "$ld:ai:tokens:total";
    private const string TokenInput = "$ld:ai:tokens:input";
    private const string TokenOutput = "$ld:ai:tokens:output";

    /// <summary>
    ///
    /// </summary>
    /// <param name="client"></param>
    /// <param name="config"></param>
    /// <param name="context"></param>
    /// <param name="key"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public LdAiConfigTracker(ILaunchDarklyClient client, LdAiConfig config, Context context, string key)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _context = context;
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _trackData =  LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            { "versionKey", LdValue.Of(Config.VersionKey)},
            { "configKey" , LdValue.Of(_key) }
        });
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="duration"></param>
    public void TrackDuration(float duration) =>
        _client.Track(Duration, _context, _trackData, duration);


    /// <summary>
    ///
    /// </summary>
    /// <param name="task"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
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
    ///
    /// </summary>
    /// <param name="feedback"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
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
    ///
    /// </summary>
    public void TrackSuccess()
    {
        _client.Track(Generation, _context, _trackData, 1);
    }


    /// <summary>
    ///
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
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
    ///
    /// </summary>
    /// <param name="usage"></param>
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


    /// <summary>
    /// TBD
    /// </summary>
    public void Dispose() => _client.Dispose();
}
