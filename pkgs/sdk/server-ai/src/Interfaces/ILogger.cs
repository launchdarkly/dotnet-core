namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Log interface required by the AI Client.
/// </summary>
public interface ILogger
{

    /// <summary>
    /// Log an error.
    /// </summary>
    /// <param name="format">format string</param>
    /// <param name="allParams">parameters</param>
    void Error(string format, params object[] allParams);

    /// <summary>
    /// Log a warning.
    /// </summary>
    /// <param name="format">format string</param>
    /// <param name="allParams">parameters</param>
    void Warn(string format, params object[] allParams);
}
