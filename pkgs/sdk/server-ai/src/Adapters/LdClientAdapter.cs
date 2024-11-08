using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Adapters;

/// <summary>
/// Adapts an <see cref="LdClient"/> to the requirements of <see cref="LdAiClient"/>.
/// </summary>
public class LdClientAdapter : ILaunchDarklyClient
{
    private readonly LdClient _client;

    /// <summary>
    /// Constructs the adapter from an existing client.
    /// </summary>
    /// <param name="client">the adapter</param>
    public LdClientAdapter(LdClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public LdValue JsonVariation(string key, Context context, LdValue defaultValue)
        => _client.JsonVariation(key, context, defaultValue);

    /// <inheritdoc/>
    public void Track(string name, Context context, LdValue data, double metricValue)
        => _client.Track(name, context, data, metricValue);

    /// <inheritdoc/>
    public ILogger GetLogger() => new LoggerAdapter(_client.GetLogger());
}
