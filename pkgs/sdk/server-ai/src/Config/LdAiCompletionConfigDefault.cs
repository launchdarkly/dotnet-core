using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.DataModel;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents a default AI Completion Config supplied by the user as a fallback to
/// <see cref="LdAiClient.CompletionConfig"/>. This type contains the same data fields
/// as <see cref="LdAiCompletionConfig"/> but has no tracker — it is purely an input
/// to the client.
///
/// Construct an instance via <see cref="New"/> and the nested <see cref="Builder"/>,
/// or use <see cref="Disabled"/> for a disabled default.
/// </summary>
public record LdAiCompletionConfigDefault
{
    /// <summary>
    /// Builder for constructing an LdAiCompletionConfigDefault instance, which can be passed
    /// as the default value to the AI Client's <see cref="LdAiClient.CompletionConfig"/> method.
    /// </summary>
    public class Builder
    {
        private bool _enabled;
        private readonly List<LdAiCompletionConfig.Message> _messages;
        private readonly Dictionary<string, LdValue> _modelParams;
        private readonly Dictionary<string, LdValue> _customModelParams;
        private string _providerName;
        private string _modelName;

        internal Builder()
        {
            _enabled = false;
            _messages = new List<LdAiCompletionConfig.Message>();
            _modelParams = new Dictionary<string, LdValue>();
            _customModelParams = new Dictionary<string, LdValue>();
            _providerName = "";
            _modelName = "";
        }

        /// <summary>
        /// Adds a message with the given content and role. The default role is <see cref="Role.User"/>.
        /// </summary>
        /// <param name="content">the content, which may contain Mustache templates</param>
        /// <param name="role">the role</param>
        /// <returns>a new builder</returns>
        public Builder AddMessage(string content, Role role = Role.User)
        {
            _messages.Add(new LdAiCompletionConfig.Message(content, role));
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
        /// Sets the model's name. By default, this will be the empty string.
        /// </summary>
        /// <param name="name">the model name</param>
        /// <returns>the builder</returns>
        public Builder SetModelName(string name)
        {
            _modelName = name;
            return this;
        }

        /// <summary>
        /// Sets the model provider's name. By default, this will be the empty string.
        /// </summary>
        /// <param name="name">the name</param>
        /// <returns>the builder</returns>
        public Builder SetModelProviderName(string name)
        {
            _providerName = name;
            return this;
        }

        /// <summary>
        /// Builds the LdAiCompletionConfigDefault instance.
        /// </summary>
        /// <returns>a new LdAiCompletionConfigDefault</returns>
        public LdAiCompletionConfigDefault Build()
        {
            return new LdAiCompletionConfigDefault(
                _enabled,
                _messages,
                new Meta(),
                new Model
                {
                    Name = _modelName,
                    Parameters = _modelParams,
                    Custom = _customModelParams
                },
                new Provider { Name = _providerName }
            );
        }
    }

    /// <summary>
    /// The prompts associated with the config.
    /// </summary>
    public readonly IReadOnlyList<LdAiCompletionConfig.Message> Messages;

    /// <summary>
    /// The model parameters associated with the config.
    /// </summary>
    public readonly LdAiCompletionConfig.ModelConfiguration Model;

    /// <summary>
    /// Information about the model provider.
    /// </summary>
    public readonly LdAiCompletionConfig.ModelProvider Provider;

    internal LdAiCompletionConfigDefault(bool enabled, IEnumerable<LdAiCompletionConfig.Message> messages, Meta meta, Model model, Provider provider)
    {
        Model = new LdAiCompletionConfig.ModelConfiguration(model?.Name ?? "", model?.Parameters ?? new Dictionary<string, LdValue>(),
            model?.Custom ?? new Dictionary<string, LdValue>());
        Messages = messages?.ToList() ?? new List<LdAiCompletionConfig.Message>();
        VariationKey = meta?.VariationKey ?? "";
        Version = meta?.Version ?? 1;
        Enabled = enabled;
        Provider = new LdAiCompletionConfig.ModelProvider(provider?.Name ?? "");
    }

    internal LdValue ToLdValue()
    {
        return LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            { "_ldMeta", LdValue.ObjectFrom(
                new Dictionary<string, LdValue>
                {
                    { "variationKey", LdValue.Of(VariationKey) },
                    { "version", LdValue.Of(Version) },
                    { "enabled", LdValue.Of(Enabled) }
                }) },
            { "messages", LdValue.ArrayFrom(Messages.Select(m => LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "content", LdValue.Of(m.Content) },
                { "role", LdValue.Of(m.Role.ToString()) }
            }))) },
            { "model", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "name", LdValue.Of(Model.Name) },
                { "parameters", LdValue.ObjectFrom(Model.Parameters) },
                { "custom", LdValue.ObjectFrom(Model.Custom) }
            }) },
            {"provider", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                {"name", LdValue.Of(Provider.Name)}
            })}
        });
    }

    /// <summary>
    /// Creates a new LdAiCompletionConfigDefault builder.
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
    public string VariationKey { get; }

    /// <summary>
    /// This field meant for internal LaunchDarkly usage.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Convenient helper that returns a disabled LdAiCompletionConfigDefault.
    /// </summary>
    public static LdAiCompletionConfigDefault Disabled => New().Disable().Build();
}
