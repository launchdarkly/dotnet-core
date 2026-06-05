using System.Collections.Generic;
using System.Collections.Immutable;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Types that compose AI Config payloads returned or accepted by the SDK.
/// </summary>
public static class LdAiConfigTypes
{
    /// <summary>
    /// Represents a single message, which is part of a prompt.
    /// </summary>
    public sealed record Message
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
    /// Represents the role of the prompt message.
    /// </summary>
    public enum Role
    {
        /// <summary>
        /// User role.
        /// </summary>
        User,

        /// <summary>
        /// System role.
        /// </summary>
        System,

        /// <summary>
        /// Assistant role.
        /// </summary>
        Assistant
    }

    /// <summary>
    /// Information about the model.
    /// </summary>
    public sealed record ModelConfig
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

        internal ModelConfig(string name, IReadOnlyDictionary<string, LdValue> parameters, IReadOnlyDictionary<string, LdValue> custom)
        {
            Name = name;
            // Materialize into an ImmutableDictionary so a consumer that downcasts to
            // IDictionary<> can't mutate the stored map. The cast still succeeds (the
            // contract is still IReadOnlyDictionary<>) but write members throw
            // NotSupportedException at runtime, matching the typed read-only promise.
            Parameters = parameters?.ToImmutableDictionary() ?? ImmutableDictionary<string, LdValue>.Empty;
            Custom = custom?.ToImmutableDictionary() ?? ImmutableDictionary<string, LdValue>.Empty;
        }
    }

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public sealed record ProviderConfig
    {
        /// <summary>
        /// The name of the model provider.
        /// </summary>
        public readonly string Name;

        internal ProviderConfig(string name)
        {
            Name = name;
        }
    }
}
