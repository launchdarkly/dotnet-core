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
    /// Builder for constructing an LdAiConfig instance, which can be passed as the default
    /// value to the AI Client's <see cref="LdAiClient.ModelConfig"/> method.
    /// </summary>
    public class Builder
    {
        private bool _enabled;
        private readonly List<Message> _messages;
        private readonly Dictionary<string, object> _modelParams;
        private string _providerId;

        internal Builder()
        {
            _enabled = false;
            _messages = new List<Message>();
            _modelParams = new Dictionary<string, object>();
            _providerId = "";
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
        /// Sets the model provider's ID. By default, this will be the empty string.
        /// </summary>
        /// <param name="id">the ID</param>
        /// <returns></returns>
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
            return new LdAiConfig(_enabled, _messages, new Meta(), _modelParams, new Provider{ Id = _providerId });
        }
    }

    /// <summary>
    /// The prompts associated with the config.
    /// </summary>
    public readonly IReadOnlyList<Message> Messages;

    /// <summary>
    /// The model parameters associated with the config.
    /// </summary>
    public readonly IReadOnlyDictionary<string, object> Model;

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public readonly ModelProvider Provider;

    internal LdAiConfig(bool enabled, IEnumerable<Message> messages, Meta meta, IReadOnlyDictionary<string, object> model, Provider provider)
    {
        Model = model ?? new Dictionary<string, object>();
        Messages = messages?.ToList() ?? new List<Message>();
        VersionKey = meta?.VersionKey ?? "";
        Enabled = enabled;
        Provider = new ModelProvider(provider?.Id ?? "");
    }

    private static LdValue ObjectToValue(object obj)
    {
        if (obj == null)
        {
            return LdValue.Null;
        }

        return obj switch
        {
            bool b => LdValue.Of(b),
            double d => LdValue.Of(d),
            string s => LdValue.Of(s),
            IEnumerable<object> list => LdValue.ArrayFrom(list.Select(ObjectToValue)),
            IDictionary<string, object> dict => LdValue.ObjectFrom(dict.ToDictionary(kv => kv.Key,
                kv => ObjectToValue(kv.Value))),
            _ => LdValue.Null
        };
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
            { "model", ObjectToValue(Model) },
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
