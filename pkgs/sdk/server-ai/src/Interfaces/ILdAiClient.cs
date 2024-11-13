using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;

namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Represents the interface of the AI client, useful for mocking.
/// </summary>
public interface ILdAiClient
{

    /// <summary>
    /// Retrieves a LaunchDarkly AI config identified by the given key. The return value
    /// is an <see cref="ILdAiConfigTracker"/>, which makes the configuration available and
    /// provides convenience methods for generating events related to model usage.
    ///
    /// Any variables provided will be interpolated into the prompt's messages.
    /// Additionally, the current LaunchDarkly context will be available as 'ldctx' within
    /// a prompt message.
    ///
    /// </summary>
    /// <param name="key">the AI config key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default config, if unable to retrieve from LaunchDarkly</param>
    /// <param name="variables">the list of variables used when interpolating the prompt</param>
    /// <returns>an AI config tracker</returns>
    public ILdAiConfigTracker ModelConfig(string key, Context context, LdAiConfig defaultValue,
        IReadOnlyDictionary<string, object> variables = null);
}
