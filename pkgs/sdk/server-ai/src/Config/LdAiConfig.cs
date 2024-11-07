using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.DataModel;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents an AI configuration, which contains model parameters and prompt messages.
/// </summary>
public record LdAiConfig
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
    /// Builder for constructing an LdAiConfig instance, which can be passed as the default
    /// value to the AI Client's <see cref="LdAiClient.ModelConfig"/> method.
    /// </summary>
    public class Builder
    {
        private bool _enabled;
        private readonly List<Message> _prompt;
        private readonly Dictionary<string, object> _modelParams;


        /// <summary>
        /// Constructs a new builder. By default, the config will be disabled, with no prompt
        /// messages or model parameters.
        /// </summary>
        public Builder()
        {
            _enabled = false;
            _prompt = new List<Message>();
            _modelParams = new Dictionary<string, object>();
        }

        /// <summary>
        /// Adds a prompt message with the given content and role. The default role is <see cref="Role.User"/>.
        /// </summary>
        /// <param name="content">the content, which may contain Mustache templates</param>
        /// <param name="role">the role</param>
        /// <returns>a new builder</returns>
        public Builder AddPromptMessage(string content, Role role = Role.User)
        {
            _prompt.Add(new Message(content, role));
            return this;
        }

        /// <summary>
        /// Disables the config.
        /// </summary>
        /// <returns>the builder</returns>
        public Builder Disable() => SetEnabled(false);

        /// <summary>
        /// Enables the config.
        /// </summary>
        /// <returns>the builder</returns>
        public Builder Enable() => SetEnabled(true);

        /// <summary>
        /// Sets the enabled state of the config based on a boolean.
        /// </summary>
        /// <param name="enabled">whether the config is enabled</param>
        /// <returns>the builder</returns>
        public Builder SetEnabled(bool enabled)
        {
            _enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets a parameter for the model. The value may be any object.
        /// </summary>
        /// <param name="name">the parameter name</param>
        /// <param name="value">the parameter value</param>
        /// <returns>the builder</returns>
        public Builder SetModelParam(string name, object value)
        {
            _modelParams[name] = value;
            return this;
        }

        /// <summary>
        /// Builds the LdAiConfig instance.
        /// </summary>
        /// <returns>a new LdAiConfig</returns>
        public LdAiConfig Build()
        {
            return new LdAiConfig(_enabled, _prompt, new Meta(), _modelParams);
        }
    }

    /// <summary>
    /// The prompts associated with the config.
    /// </summary>
    public readonly IReadOnlyList<Message> Prompt;

    /// <summary>
    /// The model parameters associated with the config.
    /// </summary>
    public readonly IReadOnlyDictionary<string, object> Model;



    internal LdAiConfig(bool enabled, IEnumerable<Message> prompt, Meta meta, IReadOnlyDictionary<string, object> model)
    {
        Model = model ?? new Dictionary<string, object>();
        Prompt = prompt?.ToList() ?? new List<Message>();
        VersionKey = meta?.VersionKey ?? "";
        Enabled = enabled;
    }


    /// <summary>
    /// Creates a new LdAiConfig builder.
    /// </summary>
    /// <returns>a new builder</returns>
    public static Builder New() => new();

    /// <summary>
    /// Returns true if the config is enabled.
    /// </summary>
    /// <returns>true if enabled</returns>
    public bool Enabled { get; }


    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    public string VersionKey { get; }

    /// <summary>
    /// Convenient helper that returns a disabled LdAiConfig.
    /// </summary>
    public static LdAiConfig Disabled = New().Disable().Build();


}
