using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.Graph;

namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Represents the interface of the AI client, useful for mocking.
/// </summary>
public interface ILdAiClient
{

    /// <summary>
    /// Retrieves a LaunchDarkly AI Completion Config identified by the given key. The return value
    /// is an <see cref="LdAiCompletionConfig"/>, which makes the configuration available and
    /// provides a <c>CreateTracker</c> method (inherited from <see cref="LdAiConfig"/>) for
    /// generating a tracker that emits events related to model usage.
    ///
    /// Any variables provided will be interpolated into the prompt's messages.
    /// Additionally, the current LaunchDarkly context will be available as 'ldctx' within
    /// a prompt message.
    ///
    /// </summary>
    /// <param name="key">the AI Completion Config key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default config, if unable to retrieve from LaunchDarkly. When not provided,
    /// a disabled config is used as the fallback.</param>
    /// <param name="variables">the list of variables used when interpolating the prompt</param>
    /// <returns>an AI Completion Config</returns>
    public LdAiCompletionConfig CompletionConfig(string key, Context context, LdAiCompletionConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null);

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
        IReadOnlyDictionary<string, object> variables = null);

    /// <summary>
    /// Retrieves a LaunchDarkly AI Agent Config identified by the given key. The return value
    /// is an <see cref="LdAiAgentConfig"/>, which provides <c>Instructions</c>, <c>Tools</c>,
    /// and a <c>CreateTracker</c> method for generating a tracker that emits model usage events.
    ///
    /// Any variables provided will be interpolated into the agent's instructions.
    /// Additionally, the current LaunchDarkly context will be available as 'ldctx' within
    /// the instructions template.
    /// </summary>
    /// <param name="key">the AI Agent Config key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default config, if unable to retrieve from LaunchDarkly. When not provided,
    /// a disabled config is used as the fallback.</param>
    /// <param name="variables">the list of variables used when interpolating the instructions</param>
    /// <returns>an AI Agent Config</returns>
    public LdAiAgentConfig AgentConfig(string key, Context context,
        LdAiAgentConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null);

    /// <summary>
    /// Retrieves multiple LaunchDarkly AI Agent Configs in a single call. Each request is
    /// evaluated independently. A single aggregate usage event is fired for the entire batch.
    /// </summary>
    /// <param name="agentConfigs">the collection of agent config requests</param>
    /// <param name="context">the context</param>
    /// <returns>a dictionary mapping each agent config key to its evaluated <see cref="LdAiAgentConfig"/></returns>
    public IReadOnlyDictionary<string, LdAiAgentConfig> AgentConfigs(
        IEnumerable<AgentConfigRequest> agentConfigs, Context context);

    /// <summary>
    /// Retrieves a LaunchDarkly AI Judge Config identified by the given key. The return value
    /// is an <see cref="LdAiJudgeConfig"/>, which provides <c>Messages</c>, <c>EvaluationMetricKey</c>,
    /// and a <c>CreateTracker</c> method for generating a tracker that emits model usage events.
    ///
    /// Any variables provided will be interpolated into the judge's prompt messages.
    /// Additionally, the current LaunchDarkly context will be available as 'ldctx' within
    /// the message templates.
    /// </summary>
    /// <param name="key">the AI Judge Config key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default config, if unable to retrieve from LaunchDarkly. When not provided,
    /// a disabled config is used as the fallback.</param>
    /// <param name="variables">the list of variables used when interpolating the messages</param>
    /// <returns>an AI Judge Config</returns>
    public LdAiJudgeConfig JudgeConfig(string key, Context context,
        LdAiJudgeConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null);

    /// <summary>
    /// Reconstructs a tracker from a resumption token. This enables cross-process scenarios
    /// such as deferred feedback, where a tracker's runId needs to be reused in a different
    /// process or at a later time.
    ///
    /// The reconstructed tracker will have empty model and provider names, as these are not
    /// included in the resumption token.
    /// </summary>
    /// <param name="resumptionToken">the resumption token obtained from <see cref="ILdAiConfigTracker.ResumptionToken"/></param>
    /// <param name="context">the context to use for track events</param>
    /// <returns>a tracker associated with the original runId</returns>
    public ILdAiConfigTracker CreateTracker(string resumptionToken, Context context);

    /// <summary>
    /// Retrieves and validates an agent graph identified by <paramref name="graphKey"/>.
    /// Fires a usage tracking event, evaluates the graph flag, fetches all agent configs
    /// referenced in the graph, and performs connectivity validation. Returns an
    /// <see cref="AgentGraphDefinition"/> whose <c>Enabled</c> property indicates whether
    /// all validation steps passed.
    /// </summary>
    /// <param name="graphKey">the LaunchDarkly flag key for the agent graph</param>
    /// <param name="context">the context</param>
    /// <param name="variables">optional variables interpolated into each node's agent instructions</param>
    /// <returns>an agent graph definition</returns>
    public AgentGraphDefinition AgentGraph(string graphKey, Context context,
        IReadOnlyDictionary<string, object> variables = null);

    /// <summary>
    /// Reconstructs a graph tracker from a resumption token. This enables cross-process
    /// continuation of graph-level metrics.
    /// </summary>
    /// <param name="resumptionToken">the token obtained from <see cref="AiGraphTracker.ResumptionToken"/></param>
    /// <param name="context">the context to use for track events</param>
    /// <returns>a graph tracker associated with the original run</returns>
    public AiGraphTracker CreateGraphTracker(string resumptionToken, Context context);
}
