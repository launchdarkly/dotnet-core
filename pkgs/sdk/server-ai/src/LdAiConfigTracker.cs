using LaunchDarkly.Sdk.Server.Ai.Config;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// TBD
/// </summary>
public class LdAiConfigTracker
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
    internal LdAiConfigTracker(ILaunchDarklyClient client, LdAiConfig config)
    {
        _client = client;
        Config = config;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public void TrackFoo()
    {
    }
}
