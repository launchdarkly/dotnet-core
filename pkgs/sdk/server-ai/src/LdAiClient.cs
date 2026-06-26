using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Graph;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// The LaunchDarkly AI client. The client is capable of retrieving AI Configs and agent graphs
/// from LaunchDarkly, and generating events specific to usage of those configs when interacting
/// with model providers.
/// </summary>
public sealed class LdAiClient : ILdAiClient, ILdAiGraphClient
{
    private readonly ILaunchDarklyClient _client;
    private readonly ConfigFactory _factory;

    private const string TrackSdkInfo = "$ld:ai:sdk:info";
    private const string TrackUsageCompletionConfig = "$ld:ai:usage:completion-config";
    private const string TrackUsageAgentConfig = "$ld:ai:usage:agent-config";
    private const string TrackUsageAgentConfigs = "$ld:ai:usage:agent-configs";
    private const string TrackUsageJudgeConfig = "$ld:ai:usage:judge-config";
    private const string TrackUsageAgentGraph = "$ld:ai:usage:agent-graph";
    private const string TrackUsageCompletionConfigTemplate = "$ld:ai:usage:completion-config-template";
    private const string TrackUsageAgentConfigTemplate = "$ld:ai:usage:agent-config-template";
    private const string TrackUsageJudgeConfigTemplate = "$ld:ai:usage:judge-config-template";

    /// <summary>
    /// Constructs a new LaunchDarkly AI client. Please note, the client library is an alpha release and is
    /// not considered ready for production use.
    ///
    /// Example:
    /// <code>
    /// var baseClient = new LdClient(Configuration.Builder("my-sdk-key").Build());
    /// var aiClient = new LdAiClient(new LdClientAdapter(baseClient));
    /// </code>
    ///
    /// </summary>
    /// <param name="client">an object satisfying <see cref="ILaunchDarklyClient"/>, such as an <see cref="LdClientAdapter"/></param>
    public LdAiClient(ILaunchDarklyClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _factory = new ConfigFactory(_client, _client.GetLogger());

        _client.Track(
            TrackSdkInfo,
            Context.Builder(ContextKind.Of("ld_ai"), "ld-internal-tracking").Anonymous(true).Build(),
            LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "aiSdkName", LdValue.Of(SdkInfo.Name) },
                { "aiSdkVersion", LdValue.Of(SdkInfo.Version) },
                { "aiSdkLanguage", LdValue.Of(SdkInfo.Language) }
            }),
            1);
    }

    private LdAiCompletionConfig EvaluateCompletion(string key, Context context,
        LdAiCompletionConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> variables,
        bool interpolate = true)
    {
        defaultValue ??= LdAiCompletionConfigDefault.Disabled;
        var ldValue = _client.JsonVariation(key, context, defaultValue.ToLdValue());
        return _factory.BuildCompletionConfig(key, ldValue, context, defaultValue, variables, interpolate);
    }

    /// <inheritdoc/>
    public LdAiCompletionConfig CompletionConfig(string key, Context context, LdAiCompletionConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null)
    {
        _client.Track(TrackUsageCompletionConfig, context, LdValue.Of(key), 1);
        return EvaluateCompletion(key, context, defaultValue, variables);
    }

    /// <summary>
    /// Retrieves a LaunchDarkly AI Completion Config identified by the given key.
    /// </summary>
    /// <param name="key">the AI Completion Config key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default config, if unable to retrieve from LaunchDarkly</param>
    /// <param name="variables">the list of variables used when interpolating the prompt</param>
    /// <returns>an AI Completion Config</returns>
    [Obsolete("Use CompletionConfig instead.")]
    public LdAiCompletionConfig Config(string key, Context context, LdAiCompletionConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null)
    {
        return CompletionConfig(key, context, defaultValue, variables);
    }

    private LdAiAgentConfig BuildAgentConfig(string key, Context context,
        LdAiAgentConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> variables,
        string graphKey = null,
        bool interpolate = true)
    {
        defaultValue ??= LdAiAgentConfigDefault.Disabled;
        var ldValue = _client.JsonVariation(key, context, defaultValue.ToLdValue());
        return _factory.BuildAgentConfig(key, ldValue, context, defaultValue, variables, graphKey, interpolate);
    }

    /// <inheritdoc/>
    public LdAiAgentConfig AgentConfig(string key, Context context,
        LdAiAgentConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null)
    {
        _client.Track(TrackUsageAgentConfig, context, LdValue.Of(key), 1);
        return BuildAgentConfig(key, context, defaultValue, variables);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, LdAiAgentConfig> AgentConfigs(
        IEnumerable<AgentConfigRequest> agentConfigs, Context context)
    {
        var requests = (agentConfigs ?? Enumerable.Empty<AgentConfigRequest>()).ToList();
        _client.Track(TrackUsageAgentConfigs, context, LdValue.Of(requests.Count), requests.Count);
        var result = new Dictionary<string, LdAiAgentConfig>();
        foreach (var req in requests)
        {
            result[req.Key] = BuildAgentConfig(req.Key, context, req.DefaultValue, req.Variables);
        }
        return result;
    }

    private LdAiJudgeConfig EvaluateJudge(string key, Context context,
        LdAiJudgeConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> variables,
        bool interpolate = true)
    {
        defaultValue ??= LdAiJudgeConfigDefault.Disabled;
        var ldValue = _client.JsonVariation(key, context, defaultValue.ToLdValue());
        return _factory.BuildJudgeConfig(key, ldValue, context, defaultValue, variables, interpolate);
    }

    /// <inheritdoc/>
    public LdAiJudgeConfig JudgeConfig(string key, Context context,
        LdAiJudgeConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null)
    {
        _client.Track(TrackUsageJudgeConfig, context, LdValue.Of(key), 1);
        return EvaluateJudge(key, context, defaultValue, variables);
    }

    /// <inheritdoc/>
    public LdAiCompletionConfig CompletionConfigTemplate(string key, Context context,
        LdAiCompletionConfigDefault defaultValue = null)
    {
        _client.Track(TrackUsageCompletionConfigTemplate, context, LdValue.Of(key), 1);
        return EvaluateCompletion(key, context, defaultValue, variables: null, interpolate: false);
    }

    /// <inheritdoc/>
    public LdAiAgentConfig AgentConfigTemplate(string key, Context context,
        LdAiAgentConfigDefault defaultValue = null)
    {
        _client.Track(TrackUsageAgentConfigTemplate, context, LdValue.Of(key), 1);
        return BuildAgentConfig(key, context, defaultValue, variables: null, interpolate: false);
    }

    /// <inheritdoc/>
    public LdAiJudgeConfig JudgeConfigTemplate(string key, Context context,
        LdAiJudgeConfigDefault defaultValue = null)
    {
        _client.Track(TrackUsageJudgeConfigTemplate, context, LdValue.Of(key), 1);
        return EvaluateJudge(key, context, defaultValue, variables: null, interpolate: false);
    }

    /// <inheritdoc/>
    public ILdAiConfigTracker CreateTracker(string resumptionToken, Context context)
    {
        return LdAiConfigTracker.FromResumptionToken(resumptionToken, _client, context);
    }

    /// <inheritdoc/>
    public AgentGraphDefinition AgentGraph(string graphKey, Context context,
        IReadOnlyDictionary<string, object> variables = null)
    {
        _client.Track(TrackUsageAgentGraph, context, LdValue.Of(graphKey), 1);

        var defaultFlagValue = LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            { "root", LdValue.Of("") }
        });
        var flagValue = _client.JsonVariation(graphKey, context, defaultFlagValue);
        var parsed = ParseAgentGraphFlagValue(flagValue);

        var variationKey = parsed.Meta?.VariationKey;
        var version = parsed.Meta?.Version ?? 1;
        AiGraphTracker TrackerFactory() => new AiGraphTracker(_client, graphKey, version, context, variationKey);

        var disabled = new AgentGraphDefinition(parsed,
            new Dictionary<string, AgentGraphNode>(),
            enabled: false,
            createTracker: TrackerFactory);

        if (parsed.Meta?.Enabled == false)
        {
            _client.GetLogger()?.Debug($"agentGraph: graph \"{graphKey}\" is disabled.");
            return disabled;
        }

        if (string.IsNullOrEmpty(parsed.Root))
        {
            _client.GetLogger()?.Debug($"agentGraph: graph \"{graphKey}\" is not fetchable or has no root node.");
            return disabled;
        }

        var allKeys = AgentGraphDefinition.CollectAllKeys(parsed);
        var reachableKeys = CollectReachableKeys(parsed);
        var unreachable = allKeys.FirstOrDefault(k => !reachableKeys.Contains(k));
        if (unreachable != null)
        {
            _client.GetLogger()?.Debug(
                $"agentGraph: graph \"{graphKey}\" has unconnected node \"{unreachable}\" that is not reachable from the root.");
            return disabled;
        }

        var agentConfigs = new Dictionary<string, LdAiAgentConfig>();
        foreach (var key in allKeys)
        {
            var config = BuildAgentConfig(key, context, null, variables, graphKey);
            if (!config.Enabled)
            {
                _client.GetLogger()?.Debug(
                    $"agentGraph: agent config \"{key}\" in graph \"{graphKey}\" is not enabled or could not be fetched.");
                return disabled;
            }
            agentConfigs[key] = config;
        }

        var nodes = AgentGraphDefinition.BuildNodes(parsed, agentConfigs);
        return new AgentGraphDefinition(parsed, nodes, enabled: true, createTracker: TrackerFactory);
    }

    /// <inheritdoc/>
    public AiGraphTracker CreateGraphTracker(string resumptionToken, Context context)
    {
        return AiGraphTracker.FromResumptionToken(resumptionToken, _client, context);
    }

    private static AgentGraphFlagValue ParseAgentGraphFlagValue(LdValue flagValue)
    {
        if (flagValue.Type != LdValueType.Object)
        {
            return new AgentGraphFlagValue { Root = "" };
        }

        var root = flagValue.Get("root").AsString ?? "";

        IReadOnlyDictionary<string, IReadOnlyList<GraphEdge>> edges = null;
        var edgesValue = flagValue.Get("edges");
        if (edgesValue.Type == LdValueType.Object)
        {
            var edgesDict = new Dictionary<string, IReadOnlyList<GraphEdge>>();
            foreach (var kv in edgesValue.Dictionary)
            {
                if (kv.Value.Type != LdValueType.Array) continue;
                var edgeList = new List<GraphEdge>();
                for (var i = 0; i < kv.Value.Count; i++)
                {
                    var edgeVal = kv.Value.Get(i);
                    if (edgeVal.Type != LdValueType.Object) continue;
                    var targetKey = edgeVal.Get("key").AsString;
                    if (string.IsNullOrEmpty(targetKey)) continue;

                    IReadOnlyDictionary<string, LdValue> handoff = null;
                    var handoffVal = edgeVal.Get("handoff");
                    if (handoffVal.Type == LdValueType.Object)
                    {
                        handoff = new ReadOnlyDictionary<string, LdValue>(
                            handoffVal.Dictionary.ToDictionary(h => h.Key, h => h.Value));
                    }

                    edgeList.Add(new GraphEdge(targetKey, handoff));
                }
                edgesDict[kv.Key] = edgeList.AsReadOnly();
            }
            edges = new ReadOnlyDictionary<string, IReadOnlyList<GraphEdge>>(edgesDict);
        }

        LdMeta meta = null;
        var metaValue = flagValue.Get("_ldMeta");
        if (metaValue.Type == LdValueType.Object)
        {
            var versionVal = metaValue.Get("version");
            var enabledVal = metaValue.Get("enabled");
            meta = new LdMeta
            {
                VariationKey = metaValue.Get("variationKey").AsString,
                Version = versionVal.IsNull ? 1 : versionVal.AsInt,
                Enabled = enabledVal.IsNull || enabledVal.AsBool
            };
        }

        return new AgentGraphFlagValue { Root = root, Edges = edges, Meta = meta };
    }

    private static HashSet<string> CollectReachableKeys(AgentGraphFlagValue flagValue)
    {
        var visited = new HashSet<string> { flagValue.Root };
        var queue = new Queue<string>();
        queue.Enqueue(flagValue.Root);

        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            if (flagValue.Edges != null && flagValue.Edges.TryGetValue(key, out var edges))
            {
                foreach (var edge in edges)
                {
                    if (visited.Add(edge.Key))
                    {
                        queue.Enqueue(edge.Key);
                    }
                }
            }
        }

        return visited;
    }
}
