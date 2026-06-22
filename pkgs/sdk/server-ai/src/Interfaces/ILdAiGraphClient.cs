using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Graph;

namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Extension interface for agent graph operations. Implemented alongside
/// <see cref="ILdAiClient"/> by <see cref="LdAiClient"/>.
/// </summary>
public interface ILdAiGraphClient
{
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
    AgentGraphDefinition AgentGraph(string graphKey, Context context,
        IReadOnlyDictionary<string, object> variables = null);

    /// <summary>
    /// Reconstructs a graph tracker from a resumption token. This enables cross-process
    /// continuation of graph-level metrics.
    /// </summary>
    /// <param name="resumptionToken">the token obtained from <see cref="AiGraphTracker.ResumptionToken"/></param>
    /// <param name="context">the context to use for track events</param>
    /// <returns>a graph tracker associated with the original run</returns>
    AiGraphTracker CreateGraphTracker(string resumptionToken, Context context);
}
