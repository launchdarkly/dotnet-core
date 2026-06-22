using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Threading;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using LaunchDarkly.Sdk.Server.Ai.Tracking;

namespace LaunchDarkly.Sdk.Server.Ai.Graph;

/// <summary>
/// Tracking metadata included in every graph tracker event.
/// </summary>
public sealed record AiGraphTrackData(
    string RunId,
    string GraphKey,
    string VariationKey,
    int Version
);

/// <summary>
/// Records metrics for a single agent graph invocation.
/// </summary>
/// <remarks>
/// All events share a <c>runId</c> so LaunchDarkly can correlate them. Graph-level
/// tracking methods (<c>TrackInvocationSuccess</c>, <c>TrackDuration</c>, etc.) are
/// at-most-once — a second call logs a warning and is silently dropped. Edge-level
/// methods (<c>TrackRedirect</c>, <c>TrackHandoffSuccess</c>, <c>TrackHandoffFailure</c>)
/// are multi-fire and may be called once per edge traversal.
/// </remarks>
public sealed class AiGraphTracker
{
    private readonly ILaunchDarklyClient _client;
    private readonly string _runId;
    private readonly string _graphKey;
    private readonly string _variationKey;
    private readonly int _version;
    private readonly Context _context;
    private readonly LdValue _trackData;
    private readonly ILogger _logger;

    private StrongBox<bool> _trackedInvocation;
    private StrongBox<double> _durationMs;
    private StrongBox<Usage> _tokens;
    private StrongBox<IReadOnlyList<string>> _path;

    private readonly Lazy<string> _resumptionToken;

    private const string GraphInvocationSuccess = "$ld:ai:graph:invocation_success";
    private const string GraphInvocationFailure = "$ld:ai:graph:invocation_failure";
    private const string GraphDuration = "$ld:ai:graph:duration:total";
    private const string GraphTotalTokens = "$ld:ai:graph:total_tokens";
    private const string GraphPath = "$ld:ai:graph:path";
    private const string GraphRedirect = "$ld:ai:graph:redirect";
    private const string GraphHandoffSuccess = "$ld:ai:graph:handoff_success";
    private const string GraphHandoffFailure = "$ld:ai:graph:handoff_failure";

    /// <summary>
    /// Constructs a new graph tracker. If <paramref name="runId"/> is null, a new UUIDv4
    /// is generated automatically.
    /// </summary>
    public AiGraphTracker(
        ILaunchDarklyClient ldClient,
        string graphKey,
        int version,
        Context context,
        string variationKey = null,
        string runId = null)
    {
        _client = ldClient ?? throw new ArgumentNullException(nameof(ldClient));
        _graphKey = graphKey ?? throw new ArgumentNullException(nameof(graphKey));
        _runId = runId ?? Guid.NewGuid().ToString();
        _variationKey = variationKey ?? "";
        _version = version;
        _context = context;
        _logger = _client.GetLogger();

        var trackDataBuilder = new Dictionary<string, LdValue>
        {
            { "runId", LdValue.Of(_runId) },
            { "graphKey", LdValue.Of(_graphKey) },
            { "version", LdValue.Of(_version) },
        };
        if (!string.IsNullOrEmpty(_variationKey))
        {
            trackDataBuilder.Add("variationKey", LdValue.Of(_variationKey));
        }
        _trackData = LdValue.ObjectFrom(trackDataBuilder);

        _resumptionToken = new Lazy<string>(BuildResumptionToken);
    }

    /// <summary>
    /// The resumption token for cross-process continuation of this tracker.
    /// </summary>
    public string ResumptionToken => _resumptionToken.Value;

    /// <summary>
    /// A partial snapshot of the metrics tracked so far. Fields are null until the
    /// corresponding track method fires.
    /// </summary>
    public AiGraphMetricSummary Summary => new AiGraphMetricSummary(
        _trackedInvocation?.Value,
        _durationMs?.Value,
        _tokens?.Value,
        _path?.Value,
        null,
        ResumptionToken
    );

    /// <summary>
    /// Returns the track data included in every event fired by this tracker.
    /// </summary>
    public AiGraphTrackData GetTrackData() =>
        new AiGraphTrackData(_runId, _graphKey,
            string.IsNullOrEmpty(_variationKey) ? null : _variationKey,
            _version);

    private string BuildResumptionToken()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("runId", _runId);
            writer.WriteString("graphKey", _graphKey);
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

    /// <summary>
    /// Reconstructs a graph tracker from a resumption token, enabling cross-process
    /// scenarios where a tracker's run ID needs to be reused.
    /// </summary>
    public static AiGraphTracker FromResumptionToken(
        string token, ILaunchDarklyClient ldClient, Context context)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));
        if (ldClient == null) throw new ArgumentNullException(nameof(ldClient));

        var base64 = token.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        GraphResumptionPayload payload;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            payload = JsonSerializer.Deserialize<GraphResumptionPayload>(json);
        }
        catch (Exception e) when (!(e is OutOfMemoryException))
        {
            throw new ArgumentException("Invalid graph resumption token", nameof(token), e);
        }

        if (payload == null || string.IsNullOrEmpty(payload.RunId) || string.IsNullOrEmpty(payload.GraphKey))
        {
            throw new ArgumentException(
                "Graph resumption token is missing required fields (runId, graphKey)",
                nameof(token));
        }

        return new AiGraphTracker(ldClient, payload.GraphKey, payload.Version ?? 1, context,
            payload.VariationKey, payload.RunId);
    }

    /// <summary>
    /// Records a successful graph invocation. At-most-once; shares a slot with
    /// <see cref="TrackInvocationFailure"/>.
    /// </summary>
    public void TrackInvocationSuccess()
    {
        if (Interlocked.CompareExchange(ref _trackedInvocation,
                new StrongBox<bool>(true), null) != null)
        {
            _logger?.Warn("Skipping TrackInvocationSuccess: invocation already recorded on this graph tracker. {0}",
                _trackData.ToJsonString());
            return;
        }
        _client.Track(GraphInvocationSuccess, _context, _trackData, 1);
    }

    /// <summary>
    /// Records a failed graph invocation. At-most-once; shares a slot with
    /// <see cref="TrackInvocationSuccess"/>.
    /// </summary>
    public void TrackInvocationFailure()
    {
        if (Interlocked.CompareExchange(ref _trackedInvocation,
                new StrongBox<bool>(false), null) != null)
        {
            _logger?.Warn("Skipping TrackInvocationFailure: invocation already recorded on this graph tracker. {0}",
                _trackData.ToJsonString());
            return;
        }
        _client.Track(GraphInvocationFailure, _context, _trackData, 1);
    }

    /// <summary>
    /// Records the total duration of the graph invocation in milliseconds. At-most-once.
    /// </summary>
    public void TrackDuration(double durationMs)
    {
        if (Interlocked.CompareExchange(ref _durationMs,
                new StrongBox<double>(durationMs), null) != null)
        {
            _logger?.Warn("Skipping TrackDuration: duration already recorded on this graph tracker. {0}",
                _trackData.ToJsonString());
            return;
        }
        _client.Track(GraphDuration, _context, _trackData, durationMs);
    }

    /// <summary>
    /// Records the aggregate token usage across all nodes. At-most-once.
    /// </summary>
    public void TrackTotalTokens(Usage tokens)
    {
        // Empty usage doesn't burn the slot.
        if ((tokens.Total ?? 0) <= 0 && (tokens.Input ?? 0) <= 0 && (tokens.Output ?? 0) <= 0)
        {
            return;
        }
        if (Interlocked.CompareExchange(ref _tokens,
                new StrongBox<Usage>(tokens), null) != null)
        {
            _logger?.Warn("Skipping TrackTotalTokens: tokens already recorded on this graph tracker. {0}",
                _trackData.ToJsonString());
            return;
        }
        var total = (tokens.Total ?? 0) > 0
            ? tokens.Total.Value
            : (tokens.Input ?? 0) + (tokens.Output ?? 0);
        _client.Track(GraphTotalTokens, _context, _trackData, total);
    }

    /// <summary>
    /// Records the path of node keys visited during graph execution. At-most-once.
    /// </summary>
    public void TrackPath(IReadOnlyList<string> path)
    {
        if (path == null) return;

        if (Interlocked.CompareExchange(ref _path,
                new StrongBox<IReadOnlyList<string>>(path.ToArray()), null) != null)
        {
            _logger?.Warn("Skipping TrackPath: path already recorded on this graph tracker. {0}",
                _trackData.ToJsonString());
            return;
        }

        var pathArray = LdValue.ArrayFrom(path.Select(LdValue.Of));
        var data = MergeTrackData("path", pathArray);
        _client.Track(GraphPath, _context, data, 1);
    }

    /// <summary>
    /// Records that a node redirected execution to a different target than the configured edge.
    /// Multi-fire — may be called once per redirect that occurs.
    /// </summary>
    public void TrackRedirect(string sourceKey, string redirectedTarget)
    {
        var data = MergeTrackData(new Dictionary<string, LdValue>
        {
            { "sourceKey", LdValue.Of(sourceKey) },
            { "redirectedTarget", LdValue.Of(redirectedTarget) }
        });
        _client.Track(GraphRedirect, _context, data, 1);
    }

    /// <summary>
    /// Records a successful handoff from one node to another.
    /// Multi-fire — may be called once per handoff that occurs.
    /// </summary>
    public void TrackHandoffSuccess(string sourceKey, string targetKey)
    {
        var data = MergeTrackData(new Dictionary<string, LdValue>
        {
            { "sourceKey", LdValue.Of(sourceKey) },
            { "targetKey", LdValue.Of(targetKey) }
        });
        _client.Track(GraphHandoffSuccess, _context, data, 1);
    }

    /// <summary>
    /// Records a failed handoff from one node to another.
    /// Multi-fire — may be called once per failed handoff that occurs.
    /// </summary>
    public void TrackHandoffFailure(string sourceKey, string targetKey)
    {
        var data = MergeTrackData(new Dictionary<string, LdValue>
        {
            { "sourceKey", LdValue.Of(sourceKey) },
            { "targetKey", LdValue.Of(targetKey) }
        });
        _client.Track(GraphHandoffFailure, _context, data, 1);
    }

    private LdValue MergeTrackData(string key, LdValue value)
    {
        var builder = new Dictionary<string, LdValue>(_trackData.Dictionary);
        builder[key] = value;
        return LdValue.ObjectFrom(builder);
    }

    private LdValue MergeTrackData(Dictionary<string, LdValue> extra)
    {
        var builder = new Dictionary<string, LdValue>(_trackData.Dictionary);
        foreach (var kv in extra)
        {
            builder[kv.Key] = kv.Value;
        }
        return LdValue.ObjectFrom(builder);
    }

    private class GraphResumptionPayload
    {
        [JsonPropertyName("runId")]
        public string RunId { get; set; }

        [JsonPropertyName("graphKey")]
        public string GraphKey { get; set; }

        [JsonPropertyName("variationKey")]
        public string VariationKey { get; set; }

        [JsonPropertyName("version")]
        public int? Version { get; set; }
    }
}
