namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// TBD
/// </summary>
public interface ILaunchDarklyClient
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="key"></param>
    /// <param name="context"></param>
    /// <param name="defaultValue"></param>
    /// <returns></returns>
    EvaluationDetail<LdValue> JsonVariationDetail(string key, Context context, LdValue defaultValue);


    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="name"></param>
    /// <param name="context"></param>
    /// <param name="data"></param>
    /// <param name="metricValue"></param>
    void Track(string name, Context context, LdValue data, double metricValue);

    /// <summary>
    /// TBD
    /// </summary>
    void Dispose();

    /// <summary>
    /// TBD
    /// </summary>
    /// <returns></returns>
    ILogger GetLogger();

}
