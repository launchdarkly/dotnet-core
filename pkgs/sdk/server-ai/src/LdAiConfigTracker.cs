using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// Records metrics for a single AI run.
/// </summary>
/// <remarks>
/// All events a tracker emits share a runId (a UUIDv4) so LaunchDarkly can correlate
/// them in metrics views. See individual track methods for their specific semantics.
/// Call <c>CreateTracker</c> on the AI Config to start a new run. A
/// <see cref="ResumptionToken"/> preserves the runId, so events emitted by a tracker
/// reconstructed in another process correlate with the original tracker's runId.
/// </remarks>
public class LdAiConfigTracker : ILdAiConfigTracker
{
    private readonly ILaunchDarklyClient _client;
    private readonly string _runId;
    private readonly string _configKey;
    private readonly string _graphKey;
    private readonly string _variationKey;
    private readonly int _version;
    private readonly Context _context;
    private readonly string _modelName;
    private readonly string _providerName;
    private readonly LdValue _trackData;
    private readonly ILogger _logger;

    // StrongBox-wrapped slots let TrackX methods claim a slot via
    // Interlocked.CompareExchange so the at-most-once semantics hold even when two threads
    // race into the same method. A non-null slot means the metric has been recorded.
    private StrongBox<double> _durationMs;
    private StrongBox<double> _timeToFirstTokenMs;
    private StrongBox<Usage> _tokens;
    private StrongBox<Feedback> _feedback;
    private StrongBox<bool> _trackedSuccess; // true = success, false = error

    // Accumulates tool keys from TrackToolCall calls. Guarded by a lock since tool calls
    // have no at-most-once semantics and may be called concurrently.
    private readonly List<string> _toolCallKeys = new();

    // Lazy<T> caches the encoded token so repeated reads avoid re-encoding the immutable
    // payload. All resumption-token inputs are readonly for a tracker's lifetime.
    private readonly Lazy<string> _resumptionToken;

    private const string Duration = "$ld:ai:duration:total";
    private const string FeedbackPositive = "$ld:ai:feedback:user:positive";
    private const string FeedbackNegative = "$ld:ai:feedback:user:negative";
    private const string GenerationSuccess = "$ld:ai:generation:success";
    private const string GenerationError = "$ld:ai:generation:error";
    private const string TokenTotal = "$ld:ai:tokens:total";
    private const string TokenInput = "$ld:ai:tokens:input";
    private const string TokenOutput = "$ld:ai:tokens:output";
    private const string TimeToFirstToken = "$ld:ai:tokens:ttf";
    private const string ToolCall = "$ld:ai:tool_call";

    /// <summary>
    /// Constructs a tracker from individual fields, ordered as defined by the AI SDK spec.
    /// Trackers are produced via <see cref="LdAiConfig.CreateTracker"/> (server-side
    /// evaluation) or <see cref="FromResumptionToken"/> (cross-process resumption); both
    /// funnel through this single constructor.
    /// </summary>
    internal LdAiConfigTracker(ILaunchDarklyClient client, string runId, string configKey,
        string variationKey, int version, Context context, string modelName, string providerName,
        string modelKey = null, int modelVersion = 1, string graphKey = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _configKey = configKey ?? throw new ArgumentNullException(nameof(configKey));
        _runId = runId ?? "";
        _graphKey = graphKey ?? "";
        _variationKey = variationKey ?? "";
        _version = version;
        _context = context;
        _modelName = modelName ?? "";
        _providerName = providerName ?? "";
        _logger = _client.GetLogger();

        var trackDataBuilder = new Dictionary<string, LdValue>
        {
            { "runId", LdValue.Of(_runId) },
            { "configKey", LdValue.Of(_configKey) },
            { "version", LdValue.Of(_version) },
            { "modelName", LdValue.Of(_modelName) },
            { "providerName", LdValue.Of(_providerName) },
            { "modelVersion", LdValue.Of(modelVersion) },
        };
        if (!string.IsNullOrEmpty(_graphKey))
        {
            trackDataBuilder.Add("graphKey", LdValue.Of(_graphKey));
        }
        if (!string.IsNullOrEmpty(_variationKey))
        {
            trackDataBuilder.Add("variationKey", LdValue.Of(_variationKey));
        }
        if (!string.IsNullOrEmpty(modelKey))
        {
            trackDataBuilder.Add("modelKey", LdValue.Of(modelKey));
        }
        _trackData = LdValue.ObjectFrom(trackDataBuilder);

        _resumptionToken = new Lazy<string>(BuildResumptionToken);
    }

    /// <inheritdoc/>
    public string ResumptionToken => _resumptionToken.Value;

    private string BuildResumptionToken()
    {
        // Utf8JsonWriter gives stable key ordering and avoids the runtime cost of
        // anonymous-type reflection. The wire format omits empty optional fields so that
        // resumption tokens round-trip exactly for configs that never carried them.
        // modelName, providerName, modelKey, and modelVersion are not included.
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
            if (!string.IsNullOrEmpty(_graphKey))
            {
                writer.WriteString("graphKey", _graphKey);
            }
            writer.WriteEndObject();
        }
        var base64 = Convert.ToBase64String(stream.ToArray());
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <inheritdoc/>
    public MetricSummary Summary
    {
        get
        {
            IReadOnlyList<string> toolCalls;
            lock (_toolCallKeys)
            {
                toolCalls = _toolCallKeys.Count > 0 ? _toolCallKeys.ToImmutableArray() : null;
            }
            return new MetricSummary(
                _durationMs?.Value,
                _feedback?.Value,
                _tokens?.Value,
                _trackedSuccess?.Value,
                _timeToFirstTokenMs?.Value,
                toolCalls,
                ResumptionToken
            );
        }
    }

    /// <inheritdoc/>
    public void TrackDuration(double durationMs)
    {
        if (Interlocked.CompareExchange(ref _durationMs,
                new StrongBox<double>(durationMs), null) != null)
        {
            _logger?.Warn("Skipping TrackDuration: duration already recorded on this tracker. Call CreateTracker on the AI Config for a new run. {0}", _trackData.ToJsonString());
            return;
        }
        _client.Track(Duration, _context, _trackData, durationMs);
    }


    /// <inheritdoc/>
    public async Task<T> TrackDurationOf<T>(Func<Task<T>> operation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await operation();
        }
        finally
        {
            sw.Stop();
            TrackDuration(sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <inheritdoc/>
    [Obsolete("Use TrackDurationOf instead.")]
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
        if (Interlocked.CompareExchange(ref _timeToFirstTokenMs,
                new StrongBox<double>(timeToFirstTokenMs), null) != null)
        {
            _logger?.Warn("Skipping TrackTimeToFirstToken: time-to-first-token already recorded on this tracker. Call CreateTracker on the AI Config for a new run. {0}", _trackData.ToJsonString());
            return;
        }
        _client.Track(TimeToFirstToken, _context, _trackData, timeToFirstTokenMs);
    }

    /// <inheritdoc/>
    public void TrackFeedback(Feedback feedback)
    {
        // Validate the enum value first so invalid input throws without consuming the slot.
        string eventName;
        switch (feedback)
        {
            case Feedback.Positive:
                eventName = FeedbackPositive;
                break;
            case Feedback.Negative:
                eventName = FeedbackNegative;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feedback), feedback, null);
        }
        if (Interlocked.CompareExchange(ref _feedback,
                new StrongBox<Feedback>(feedback), null) != null)
        {
            _logger?.Warn("Skipping TrackFeedback: feedback already recorded on this tracker. Call CreateTracker on the AI Config for a new run. {0}", _trackData.ToJsonString());
            return;
        }
        _client.Track(eventName, _context, _trackData, 1);
    }

    /// <inheritdoc/>
    public void TrackSuccess()
    {
        if (Interlocked.CompareExchange(ref _trackedSuccess,
                new StrongBox<bool>(true), null) != null)
        {
            _logger?.Warn("Skipping TrackSuccess: success/error already recorded on this tracker. Call CreateTracker on the AI Config for a new run. {0}", _trackData.ToJsonString());
            return;
        }
        _client.Track(GenerationSuccess, _context, _trackData, 1);
    }

    /// <inheritdoc/>
    public void TrackError()
    {
        if (Interlocked.CompareExchange(ref _trackedSuccess,
                new StrongBox<bool>(false), null) != null)
        {
            _logger?.Warn("Skipping TrackError: success/error already recorded on this tracker. Call CreateTracker on the AI Config for a new run. {0}", _trackData.ToJsonString());
            return;
        }
        _client.Track(GenerationError, _context, _trackData, 1);
    }

    /// <inheritdoc/>
    public async Task<T> TrackMetricsOf<T>(Func<T, AiMetrics> metricsExtractor, Func<Task<T>> operation)
    {
        var sw = Stopwatch.StartNew();
        T result;
        try
        {
            result = await operation();
        }
        catch (Exception)
        {
            sw.Stop();
            TrackDuration(sw.Elapsed.TotalMilliseconds);
            TrackError();
            throw;
        }

        // Capture elapsed immediately so a slow extractor doesn't inflate the metric.
        sw.Stop();
        var operationElapsedMs = sw.Elapsed.TotalMilliseconds;

        // Extractor failure: track duration but NOT error — the AI operation itself succeeded.
        // Matches Java's LDAIConfigTrackerImpl.trackMetricsOf behavior.
        AiMetrics metrics;
        try
        {
            metrics = metricsExtractor(result);
        }
        catch (Exception)
        {
            TrackDuration(operationElapsedMs);
            throw;
        }

        // Honor an explicit duration override from the caller; fall back to the measured value.
        TrackDuration(metrics.DurationMs ?? operationElapsedMs);

        if (metrics.Success)
        {
            TrackSuccess();
        }
        else
        {
            TrackError();
        }

        if (metrics.Tokens != null)
        {
            TrackTokens(metrics.Tokens.Value);
        }

        if (metrics.ToolCalls?.Count > 0)
        {
            TrackToolCalls(metrics.ToolCalls);
        }

        return result;
    }

    /// <inheritdoc/>
    [Obsolete("Use TrackMetricsOf instead.")]
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
        // Empty usage doesn't burn the slot.
        if ((usage.Total ?? 0) <= 0 && (usage.Input ?? 0) <= 0 && (usage.Output ?? 0) <= 0)
        {
            return;
        }
        // Atomic claim.
        if (Interlocked.CompareExchange(ref _tokens,
                new StrongBox<Usage>(usage), null) != null)
        {
            _logger?.Warn("Skipping TrackTokens: tokens already recorded on this tracker. Call CreateTracker on the AI Config for a new run. {0}", _trackData.ToJsonString());
            return;
        }
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

    /// <inheritdoc/>
    public void TrackJudgeResult(JudgeResult result)
    {
        if (!result.Sampled || !result.Success)
        {
            return;
        }

        var data = string.IsNullOrEmpty(result.JudgeConfigKey)
            ? _trackData
            : MergeTrackData("judgeConfigKey", LdValue.Of(result.JudgeConfigKey));

        _client.Track(result.MetricKey, _context, data, result.Score);
    }

    /// <inheritdoc/>
    public void TrackToolCall(string toolKey)
    {
        lock (_toolCallKeys) { _toolCallKeys.Add(toolKey); }
        var data = MergeTrackData("toolKey", LdValue.Of(toolKey));
        _client.Track(ToolCall, _context, data, 1);
    }

    /// <inheritdoc/>
    public void TrackToolCalls(IEnumerable<string> toolKeys)
    {
        foreach (var key in toolKeys)
        {
            TrackToolCall(key);
        }
    }

    private LdValue MergeTrackData(string key, LdValue value)
    {
        var builder = new Dictionary<string, LdValue>(_trackData.Dictionary);
        builder[key] = value;
        return LdValue.ObjectFrom(builder);
    }

    /// <summary>
    /// Reconstructs a tracker from a resumption token. This enables cross-process scenarios
    /// such as deferred feedback, where a tracker's runId needs to be reused in a different
    /// process or at a later time.
    ///
    /// The reconstructed tracker will have empty model and provider names, as these are not
    /// included in the resumption token. modelKey and modelVersion are also not included;
    /// modelVersion defaults to 1 on reconstructed trackers.
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
        // Catch any parse-surface exception (custom converters, encoding faults) and
        // wrap as ArgumentException; only OutOfMemoryException is allowed to escape.
        catch (Exception e) when (!(e is OutOfMemoryException))
        {
            throw new ArgumentException("Invalid resumption token", nameof(token), e);
        }

        if (payload == null || string.IsNullOrEmpty(payload.RunId) || string.IsNullOrEmpty(payload.ConfigKey))
        {
            throw new ArgumentException("Resumption token is missing required fields (runId, configKey)",
                nameof(token));
        }

        return new LdAiConfigTracker(client, payload.RunId, payload.ConfigKey,
            payload.VariationKey, payload.Version ?? 1, context, "", "", graphKey: payload.GraphKey);
    }

    private class ResumptionPayload
    {
        [JsonPropertyName("runId")]
        public string RunId { get; set; }

        [JsonPropertyName("configKey")]
        public string ConfigKey { get; set; }

        [JsonPropertyName("graphKey")]
        public string GraphKey { get; set; }

        [JsonPropertyName("variationKey")]
        public string VariationKey { get; set; }

        [JsonPropertyName("version")]
        public int? Version { get; set; }
    }
}
