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

    private const string Duration = "$ld:ai:duration:total";
    private const string FeedbackPositive = "$ld:ai:feedback:user:positive";
    private const string FeedbackNegative = "$ld:ai:feedback:user:negative";
    private const string Generation = "$ld:ai:generation";

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
    }

    private LdValue GetTrackData()
    {
        return LdValue.ObjectFrom(new Dictionary<string, LdValue>
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
        _client.Track(Duration, _context, GetTrackData(), duration);


    /// <summary>
    ///
    /// </summary>
    /// <param name="task"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public async Task<T> TrackDurationOfTask<T>(Task<T> task)
    {
        var sw = Stopwatch.StartNew();
        var result = await task;
        sw.Stop();
        TrackDuration(sw.ElapsedMilliseconds);
        return result;
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
                _client.Track(FeedbackPositive, _context, GetTrackData(), 1);
                break;
            case Feedback.Negative:
                _client.Track(FeedbackNegative, _context, GetTrackData(), 1);
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
        _client.Track(Generation, _context, GetTrackData(), 1);
    }


    /// <summary>
    /// TBD
    /// </summary>
    public void Dispose() => _client.Dispose();
}
