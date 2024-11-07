namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Interface representing capabilities needed by the AI Client. These are usually provided
/// by the LaunchDarkly Server SDK.
/// </summary>
public interface ILaunchDarklyClient
{
    /// <summary>
    /// Returns a JSON variation, with detail.
    /// </summary>
    /// <param name="key">the flag key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default value</param>
    /// <returns>a detailed evaluation</returns>
    EvaluationDetail<LdValue> JsonVariationDetail(string key, Context context, LdValue defaultValue);


    /// <summary>
    /// Tracks a metric.
    /// </summary>
    /// <param name="name">metric name</param>
    /// <param name="context">context</param>
    /// <param name="data">associated data</param>
    /// <param name="metricValue">metric value</param>
    void Track(string name, Context context, LdValue data, double metricValue);

    /// <summary>
    /// Disposes the client.
    /// </summary>
    void Dispose();

    /// <summary>
    /// Returns a logger.
    /// </summary>
    /// <returns>a logger</returns>
    ILogger GetLogger();

}
