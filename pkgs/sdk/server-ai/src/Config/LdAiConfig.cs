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
    /// Information about the model provider.
    /// </summary>
    public record ModelProvider
    {
        /// <summary>
        /// The ID of the model provider.
        /// </summary>
        public readonly string Id;

        internal ModelProvider(string id)
        {
            Id = id;
        }
    }

    /// <summary>
    /// Information about the model.
    /// </summary>
    public record ModelConfiguration
    {
        /// <summary>
        /// The ID of the model.
        /// </summary>
        public readonly string Id;

        /// <summary>
        /// The model's built-in parameters provided by LaunchDarkly.
        /// </summary>
        public readonly IReadOnlyDictionary<string, LdValue> Parameters;

        /// <summary>
        /// The model's custom parameters provided by the user.
        /// </summary>
        public readonly IReadOnlyDictionary<string, LdValue> Custom;

        internal ModelConfiguration(string id, IReadOnlyDictionary<string, LdValue> parameters, IReadOnlyDictionary<string, LdValue> custom)
        {
            Id = id;
            Parameters = parameters;
            Custom = custom;
        }
    }

    /// <summary>
    /// Builder for constructing an LdAiConfig instance, which can be passed as the default
    /// value to the AI Client's <see cref="LdAiClient.Config"/> method.
    /// </summary>
    public class Builder
    {
        private bool _enabled;
        private readonly List<Message> _messages;
        private readonly Dictionary<string, LdValue> _modelParams;
        private readonly Dictionary<string, LdValue> _customModelParams;
        private string _providerId;
        private string _modelId;

        internal Builder()
        {
            _enabled = false;
            _messages = new List<Message>();
            _modelParams = new Dictionary<string, LdValue>();
            _customModelParams = new Dictionary<string, LdValue>();
            _providerId = "";
            _modelId = "";
        }

        /// <summary>
        /// Adds a message with the given content and role. The default role is <see cref="Role.User"/>.
        /// </summary>
        /// <param name="content">the content, which may contain Mustache templates</param>
        /// <param name="role">the role</param>
        /// <returns>a new builder</returns>
        public Builder AddMessage(string content, Role role = Role.User)
        {
            _messages.Add(new Message(content, role));
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
        /// Sets a parameter for the model.
        /// </summary>
        /// <param name="name">the parameter name</param>
        /// <param name="value">the parameter value</param>
        /// <returns>the builder</returns>
        public Builder SetModelParam(string name, LdValue value)
        {
            _modelParams[name] = value;
            return this;
        }

        /// <summary>
        /// Sets a custom parameter for the model.
        /// </summary>
        /// <param name="name">the custom parameter name</param>
        /// <param name="value">the custom parameter value</param>
        /// <returns>the builder</returns>
        public Builder SetCustomModelParam(string name, LdValue value)
        {
            _customModelParams[name] = value;
            return this;
        }

        /// <summary>
        /// Sets the model's ID. By default, this will be the empty string.
        /// </summary>
        /// <param name="id">the model ID</param>
        /// <returns>the builder</returns>
        public Builder SetModelId(string id)
        {
            _modelId = id;
            return this;
        }

        /// <summary>
        /// Sets the model provider's ID. By default, this will be the empty string.
        /// </summary>
        /// <param name="id">the ID</param>
        /// <returns>the builder</returns>
        public Builder SetModelProviderId(string id)
        {
            _providerId = id;
            return this;
        }

        /// <summary>
        /// Builds the LdAiConfig instance.
        /// </summary>
        /// <returns>a new LdAiConfig</returns>
        public LdAiConfig Build()
        {
            return new LdAiConfig(
                _enabled,
                _messages,
                new Meta(),
                new Model
                {
                    Id = _modelId,
                    Parameters = _modelParams,
                    Custom = _customModelParams
                },
                new Provider{ Id = _providerId }
            );
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

    internal LdAiConfig(bool enabled, IEnumerable<Message> messages, Meta meta, Model model, Provider provider)
    {
        Model = new ModelConfiguration(model?.Id ?? "", model?.Parameters ?? new Dictionary<string, LdValue>(),
            model?.Custom ?? new Dictionary<string, LdValue>());
        Messages = messages?.ToList() ?? new List<Message>();
        VersionKey = meta?.VersionKey ?? "";
        Enabled = enabled;
        Provider = new ModelProvider(provider?.Id ?? "");
    }
    internal LdValue ToLdValue()
    {
        return LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            { "_ldMeta", LdValue.ObjectFrom(
                new Dictionary<string, LdValue>
                {
                    { "versionKey", LdValue.Of(VersionKey) },
                    { "enabled", LdValue.Of(Enabled) }
                }) },
            { "messages", LdValue.ArrayFrom(Messages.Select(m => LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "content", LdValue.Of(m.Content) },
                { "role", LdValue.Of(m.Role.ToString()) }
            }))) },
            { "model", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "id", LdValue.Of(Model.Id) },
                { "parameters", LdValue.ObjectFrom(Model.Parameters) },
                { "custom", LdValue.ObjectFrom(Model.Custom) }
            }) },
            {"provider", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                {"id", LdValue.Of(Provider.Id)}
            })}
        });
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
    public static LdAiConfig Disabled => New().Disable().Build();
}
