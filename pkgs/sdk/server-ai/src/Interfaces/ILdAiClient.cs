using System;
using System.Collections.Generic;
using LaunchDarkly.Sdk.Server.Ai.Config;

namespace LaunchDarkly.Sdk.Server.Ai.Interfaces;

/// <summary>
/// Represents the interface of the AI client, useful for mocking.
/// </summary>
public interface ILdAiClient
{

    /// <summary>
    /// Retrieves a LaunchDarkly AI Completion Config identified by the given key. The return value
    /// is an <see cref="ILdAiConfigTracker"/>, which makes the configuration available and
    /// provides convenience methods for generating events related to model usage.
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
    /// <returns>an AI Completion Config tracker</returns>
    public ILdAiConfigTracker CompletionConfig(string key, Context context, LdAiConfig defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null);

    /// <summary>
    /// Retrieves a LaunchDarkly AI Completion Config identified by the given key.
    /// </summary>
    /// <param name="key">the AI Completion Config key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default config, if unable to retrieve from LaunchDarkly</param>
    /// <param name="variables">the list of variables used when interpolating the prompt</param>
    /// <returns>an AI Completion Config tracker</returns>
    [Obsolete("Use CompletionConfig instead.")]
    public ILdAiConfigTracker Config(string key, Context context, LdAiConfig defaultValue = null,
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
}
