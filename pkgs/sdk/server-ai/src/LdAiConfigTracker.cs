using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// A tracker capable of reporting events related to a particular AI Config.
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
    private const string GenerationSuccess = "$ld:ai:generation:success";
    private const string GenerationError = "$ld:ai:generation:error";
    private const string TokenTotal = "$ld:ai:tokens:total";
    private const string TokenInput = "$ld:ai:tokens:input";
    private const string TokenOutput = "$ld:ai:tokens:output";
    private const string TimeToFirstToken = "$ld:ai:tokens:ttf";

    /// <summary>
    /// Constructs a new AI Config tracker. The tracker is associated with a configuration,
    /// a context, and a key which identifies the configuration.
    /// </summary>
    /// <param name="client">the LaunchDarkly client</param>
    /// <param name="configKey">key of the AI Config</param>
    /// <param name="config">the AI Config</param>
    /// <param name="context">the context</param>
    /// <exception cref="ArgumentNullException"></exception>
    public LdAiConfigTracker(ILaunchDarklyClient client, string configKey, LdAiConfig config, Context context)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _context = context;
        _trackData =  LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            { "variationKey", LdValue.Of(config.VariationKey)},
            { "version", LdValue.Of(config.Version)},
            { "configKey" , LdValue.Of(configKey ?? throw new ArgumentNullException(nameof(configKey))) },
            { "modelName", LdValue.Of(config.Model?.Name) },
            { "providerName", LdValue.Of(config.Provider?.Name) },
            { "providerName", LdValue.Of(config.Provider.Name) },
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
        var sw = Stopwatch.StartNew();
        try {
            return await task;
        } finally {
            sw.Stop();
            TrackDuration(sw.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc/>
    public void TrackTimeToFirstToken(float timeToFirstTokenMs) =>
        _client.Track(TimeToFirstToken, _context, _trackData, timeToFirstTokenMs);

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
        _client.Track(GenerationSuccess, _context, _trackData, 1);
        _client.Track(Generation, _context, _trackData, 1);
    }

    /// <inheritdoc/>
    public void TrackError()
    {
        _client.Track(GenerationError, _context, _trackData, 1);
        _client.Track(Generation, _context, _trackData, 1);
    }

    /// <inheritdoc/>
    public async Task<Response> TrackRequest(Task<Response> request)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await request;
            TrackSuccess();

            sw.Stop();
            TrackDuration(result.Metrics?.LatencyMs ?? sw.ElapsedMilliseconds);

            if (result.Usage != null)
            {
                TrackTokens(result.Usage.Value);
            }

            return result;
        }
        catch (Exception)
        {
            sw.Stop();
            TrackDuration(sw.ElapsedMilliseconds);
            TrackError();
            throw;
        }

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
