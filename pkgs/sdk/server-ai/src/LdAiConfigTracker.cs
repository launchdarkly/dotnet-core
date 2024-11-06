using System;
using LaunchDarkly.Sdk.Server.Ai.Config;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// TBD
/// </summary>
public class LdAiConfigTracker : IDisposable
{
    /// <summary>
    /// TBD
    /// </summary>
    public readonly LdAiConfig Config;

    /// <summary>
    /// TBD
    /// </summary>
    private ILaunchDarklyClient _client;

    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="client"></param>
    /// <param name="config"></param>
    public LdAiConfigTracker(ILaunchDarklyClient client, LdAiConfig config)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// TBD
    /// </summary>
    public void Dispose() => _client.Dispose();
}
