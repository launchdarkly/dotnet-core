using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents an AI Completion Config returned by the SDK, containing model parameters,
/// prompt messages, and a factory that produces a tracker for reporting events related
/// to model usage.
///
/// Instances of this type are produced by <see cref="LdAiClient.CompletionConfig"/>;
/// they are not constructed directly by users. To supply a fallback default to the
/// client, use <see cref="LdAiCompletionConfigDefault"/>.
/// </summary>
public record LdAiCompletionConfig
{
    /// <summary>
    /// Represents a single message, which is part of a prompt.
    /// </summary>
    public record Message
    {
        /// <summary>
        /// The content of the message, which may contain Mustache templates.
        /// </summary>
        public readonly string Content;

        /// <summary>
        /// The role of the message.
        /// </summary>
        public readonly Role Role;

        internal Message(string content, Role role)
        {
            Content = content;
            Role = role;
        }
    }


    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public record ModelProvider
    {
        /// <summary>
        /// The name of the model provider.
        /// </summary>
        public readonly string Name;

        internal ModelProvider(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Information about the model.
    /// </summary>
    public record ModelConfiguration
    {
        /// <summary>
        /// The name of the model.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The model's built-in parameters provided by LaunchDarkly.
        /// </summary>
        public readonly IReadOnlyDictionary<string, LdValue> Parameters;

        /// <summary>
        /// The model's custom parameters provided by the user.
        /// </summary>
        public readonly IReadOnlyDictionary<string, LdValue> Custom;

        internal ModelConfiguration(string name, IReadOnlyDictionary<string, LdValue> parameters, IReadOnlyDictionary<string, LdValue> custom)
        {
            Name = name;
            Parameters = parameters;
            Custom = custom;
        }
    }

    /// <summary>
    /// The prompts associated with the config.
    /// </summary>
    public readonly IReadOnlyList<Message> Messages;

    /// <summary>
    /// The model parameters associated with the config.
    /// </summary>
    public readonly ModelConfiguration Model;

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public readonly ModelProvider Provider;

    private readonly Func<LdAiCompletionConfig, ILdAiConfigTracker> _trackerFactory;

    internal LdAiCompletionConfig(bool enabled, IEnumerable<Message> messages, Meta meta, Model model, Provider provider,
        Func<LdAiCompletionConfig, ILdAiConfigTracker> trackerFactory)
    {
        Model = new ModelConfiguration(model?.Name ?? "", model?.Parameters ?? new Dictionary<string, LdValue>(),
            model?.Custom ?? new Dictionary<string, LdValue>());
        Messages = messages?.ToList() ?? new List<Message>();
        VariationKey = meta?.VariationKey ?? "";
        Version = meta?.Version ?? 1;
        Enabled = enabled;
        Provider = new ModelProvider(provider?.Name ?? "");
        _trackerFactory = trackerFactory ?? throw new ArgumentNullException(nameof(trackerFactory));
    }

    /// <summary>
    /// Creates a tracker for reporting events related to this config. The returned tracker
    /// is always non-null.
    /// </summary>
    /// <returns>a tracker for this config</returns>
    public ILdAiConfigTracker CreateTracker() => _trackerFactory(this);

    /// <summary>
    /// Returns true if the config is enabled.
    /// </summary>
    /// <returns>true if enabled</returns>
    public bool Enabled { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    public string VariationKey { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    public int Version { get; }
}
