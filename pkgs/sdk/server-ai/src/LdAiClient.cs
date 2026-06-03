using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// The LaunchDarkly AI client. The client is capable of retrieving AI Configs from LaunchDarkly,
/// and generating events specific to usage of the AI Config when interacting with model providers.
/// </summary>
public sealed class LdAiClient : ILdAiClient
{
    private readonly ILaunchDarklyClient _client;
    private readonly ConfigFactory _factory;

    private const string TrackSdkInfo = "$ld:ai:sdk:info";
    private const string TrackUsageCompletionConfig = "$ld:ai:usage:completion-config";
    private const string TrackUsageAgentConfig = "$ld:ai:usage:agent-config";
    private const string TrackUsageAgentConfigs = "$ld:ai:usage:agent-configs";
    private const string TrackUsageJudgeConfig = "$ld:ai:usage:judge-config";

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

    /// <inheritdoc/>
    public LdAiCompletionConfig CompletionConfig(string key, Context context, LdAiCompletionConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null)
    {
        _client.Track(TrackUsageCompletionConfig, context, LdValue.Of(key), 1);
        defaultValue ??= LdAiCompletionConfigDefault.Disabled;

        var ldValue = _client.JsonVariation(key, context, defaultValue.ToLdValue());
        return _factory.BuildCompletionConfig(key, ldValue, context, defaultValue, variables);
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

    /// <inheritdoc/>
    public LdAiAgentConfig AgentConfig(string key, Context context,
        LdAiAgentConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null)
    {
        defaultValue ??= LdAiAgentConfigDefault.Disabled;
        _client.Track(TrackUsageAgentConfig, context, LdValue.Of(key), 1);
        var ldValue = _client.JsonVariation(key, context, defaultValue.ToLdValue());
        return _factory.BuildAgentConfig(key, ldValue, context, defaultValue, variables);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, LdAiAgentConfig> AgentConfigs(
        IEnumerable<AgentConfigRequest> agentConfigs, Context context)
    {
        var result = new Dictionary<string, LdAiAgentConfig>();
        foreach (var req in agentConfigs ?? Enumerable.Empty<AgentConfigRequest>())
        {
            result[req.Key] = AgentConfig(req.Key, context, req.DefaultValue, req.Variables);
        }
        _client.Track(TrackUsageAgentConfigs, context, LdValue.Of(result.Count), result.Count);
        return result;
    }

    /// <inheritdoc/>
    public LdAiJudgeConfig JudgeConfig(string key, Context context,
        LdAiJudgeConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null)
    {
        defaultValue ??= LdAiJudgeConfigDefault.Disabled;
        _client.Track(TrackUsageJudgeConfig, context, LdValue.Of(key), 1);
        var ldValue = _client.JsonVariation(key, context, defaultValue.ToLdValue());
        return _factory.BuildJudgeConfig(key, ldValue, context, defaultValue, variables);
    }

    /// <inheritdoc/>
    public ILdAiConfigTracker CreateTracker(string resumptionToken, Context context)
    {
        return LdAiConfigTracker.FromResumptionToken(resumptionToken, _client, context);
    }
}
