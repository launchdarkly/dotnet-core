using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Adapters;

/// <summary>
/// Adapts a <see cref="Logger"/> to the requirements of the <see cref="LdAiClient"/>'s
/// logger.
/// </summary>
internal class LoggerAdapter : ILogger
{
    private readonly Logger _logger;

    /// <summary>
    /// Creates a new adapter.
    /// </summary>
    /// <param name="logger">the existing logger</param>
    public LoggerAdapter(Logger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Error(string format, params object[] allParams) => _logger.Error(format, allParams);

    /// <inheritdoc/>
    public void Warn(string format, params object[] allParams) => _logger.Warn(format, allParams);
}
