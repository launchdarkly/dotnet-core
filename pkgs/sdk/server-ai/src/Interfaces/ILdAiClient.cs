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
    /// is an <see cref="LdAiCompletionConfig"/>, which makes the configuration available and
    /// provides a <see cref="LdAiCompletionConfig.CreateTracker"/> method for generating a tracker
    /// that emits events related to model usage.
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
    /// <returns>an AI Completion Config</returns>
    public LdAiCompletionConfig CompletionConfig(string key, Context context, LdAiCompletionConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null);

    /// <summary>
    /// Retrieves a LaunchDarkly AI Completion Config identified by the given key.
    /// </summary>
    /// <param name="key">the AI Completion Config key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default config, if unable to retrieve from LaunchDarkly</param>
    /// <param name="variables">the list of variables used when interpolating the prompt</param>
    /// <returns>an AI Completion Config</returns>
    [Obsolete("Use CompletionConfig instead.")]
    public LdAiCompletionConfig Config(string key, Context context, LdAiCompletionConfigDefault defaultValue = null,
        IReadOnlyDictionary<string, object> variables = null);
}
