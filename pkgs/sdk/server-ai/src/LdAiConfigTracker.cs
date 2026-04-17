using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly string _runId;
    private readonly string _configKey;
    private readonly string _variationKey;
    private readonly int _version;
    private readonly Context _context;
    private readonly string _modelName;
    private readonly string _providerName;
    private readonly LdValue _trackData;
    private readonly ILogger _logger;

    private double? _durationMs;
    private double? _timeToFirstTokenMs;
    private Usage? _tokens;
    private Feedback? _feedback;
    private bool? _trackedSuccess;

    private const string Duration = "$ld:ai:duration:total";
    private const string FeedbackPositive = "$ld:ai:feedback:user:positive";
    private const string FeedbackNegative = "$ld:ai:feedback:user:negative";
    private const string GenerationSuccess = "$ld:ai:generation:success";
    private const string GenerationError = "$ld:ai:generation:error";
    private const string TokenTotal = "$ld:ai:tokens:total";
    private const string TokenInput = "$ld:ai:tokens:input";
    private const string TokenOutput = "$ld:ai:tokens:output";
    private const string TimeToFirstToken = "$ld:ai:tokens:ttf";

    /// <summary>
    /// Constructs a new AI Config tracker. The tracker is associated with a run, a config key,
    /// and a context. The runId should be a unique identifier (UUID v4) for each execution.
    /// </summary>
    /// <param name="client">the LaunchDarkly client</param>
    /// <param name="runId">the unique run identifier</param>
    /// <param name="configKey">key of the AI Config</param>
    /// <param name="variationKey">the variation key</param>
    /// <param name="version">the config version</param>
    /// <param name="context">the context</param>
    /// <param name="modelName">the model name</param>
    /// <param name="providerName">the provider name</param>
    public LdAiConfigTracker(ILaunchDarklyClient client, string runId, string configKey,
        string variationKey, int version, Context context, string modelName, string providerName)
    {
        _client = client;
        _runId = runId ?? "";
        _configKey = configKey ?? "";
        _variationKey = variationKey;
        _version = version;
        _context = context;
        _modelName = modelName ?? "";
        _providerName = providerName ?? "";
        _logger = client?.GetLogger();

        var trackDataBuilder = new Dictionary<string, LdValue>
        {
            { "runId", LdValue.Of(_runId) },
            { "configKey", LdValue.Of(_configKey) },
            { "version", LdValue.Of(_version) },
            { "modelName", LdValue.Of(_modelName) },
            { "providerName", LdValue.Of(_providerName) },
        };
        if (!string.IsNullOrEmpty(_variationKey))
        {
            trackDataBuilder.Add("variationKey", LdValue.Of(_variationKey));
        }
        _trackData = LdValue.ObjectFrom(trackDataBuilder);
    }

    /// <inheritdoc/>
    public string ResumptionToken
    {
        get
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("runId", _runId);
                writer.WriteString("configKey", _configKey);
                if (!string.IsNullOrEmpty(_variationKey))
                {
                    writer.WriteString("variationKey", _variationKey);
                }
                writer.WriteNumber("version", _version);
                writer.WriteEndObject();
            }
            var base64 = Convert.ToBase64String(stream.ToArray());
            return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }

    /// <inheritdoc/>
    public MetricSummary Summary => new MetricSummary(
        _durationMs,
        _feedback,
        _tokens,
        _trackedSuccess,
        _timeToFirstTokenMs
    );

    /// <inheritdoc/>
    public void TrackDuration(float durationMs)
    {
        if (_durationMs.HasValue)
        {
            _logger?.Warn("Duration has already been tracked for this operation. [{0}]", _trackData.ToJsonString());
            return;
        }
        _durationMs = durationMs;
        _client.Track(Duration, _context, _trackData, durationMs);
    }


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
    public void TrackTimeToFirstToken(float timeToFirstTokenMs)
    {
        if (_timeToFirstTokenMs.HasValue)
        {
            _logger?.Warn("Time to first token has already been tracked for this operation. [{0}]", _trackData.ToJsonString());
            return;
        }
        _timeToFirstTokenMs = timeToFirstTokenMs;
        _client.Track(TimeToFirstToken, _context, _trackData, timeToFirstTokenMs);
    }

    /// <inheritdoc/>
    public void TrackFeedback(Feedback feedback)
    {
        if (_feedback.HasValue)
        {
            _logger?.Warn("Feedback has already been tracked for this operation. [{0}]", _trackData.ToJsonString());
            return;
        }
        _feedback = feedback;
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
        if (_trackedSuccess.HasValue)
        {
            _logger?.Warn("Generation result has already been tracked for this operation. [{0}]", _trackData.ToJsonString());
            return;
        }
        _trackedSuccess = true;
        _client.Track(GenerationSuccess, _context, _trackData, 1);
    }

    /// <inheritdoc/>
    public void TrackError()
    {
        if (_trackedSuccess.HasValue)
        {
            _logger?.Warn("Generation result has already been tracked for this operation. [{0}]", _trackData.ToJsonString());
            return;
        }
        _trackedSuccess = false;
        _client.Track(GenerationError, _context, _trackData, 1);
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
        if (_tokens.HasValue)
        {
            _logger?.Warn("Tokens have already been tracked for this operation. [{0}]", _trackData.ToJsonString());
            return;
        }
        _tokens = usage;
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
    /// Reconstructs a tracker from a resumption token. This enables cross-process scenarios
    /// such as deferred feedback, where a tracker's runId needs to be reused in a different
    /// process or at a later time.
    ///
    /// The reconstructed tracker will have empty model and provider names, as these are not
    /// included in the resumption token.
    /// </summary>
    /// <param name="token">the resumption token obtained from <see cref="ResumptionToken"/></param>
    /// <param name="client">the LaunchDarkly client</param>
    /// <param name="context">the context to use for track events</param>
    /// <returns>a new tracker associated with the original run</returns>
    /// <exception cref="ArgumentNullException">thrown if token or client is null</exception>
    /// <exception cref="ArgumentException">thrown if the token is malformed or missing required fields</exception>
    public static LdAiConfigTracker FromResumptionToken(string token, ILaunchDarklyClient client, Context context)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));
        if (client == null) throw new ArgumentNullException(nameof(client));

        var base64 = token.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        ResumptionPayload payload;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            payload = JsonSerializer.Deserialize<ResumptionPayload>(json);
        }
        catch (Exception e) when (e is FormatException || e is JsonException)
        {
            throw new ArgumentException("Invalid resumption token", nameof(token), e);
        }

        if (string.IsNullOrEmpty(payload.RunId) || string.IsNullOrEmpty(payload.ConfigKey))
        {
            throw new ArgumentException("Resumption token is missing required fields (runId, configKey)",
                nameof(token));
        }

        return new LdAiConfigTracker(client, payload.RunId, payload.ConfigKey,
            payload.VariationKey, payload.Version, context, "", "");
    }

    private class ResumptionPayload
    {
        [JsonPropertyName("runId")]
        public string RunId { get; set; }

        [JsonPropertyName("configKey")]
        public string ConfigKey { get; set; }

        [JsonPropertyName("variationKey")]
        public string VariationKey { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }
    }
}
