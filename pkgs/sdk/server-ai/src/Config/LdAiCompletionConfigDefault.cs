using System.Collections.Generic;
using System.Linq;

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
public sealed class LdAiCompletionConfigDefault : LdAiConfigDefaultBase
{
    /// <summary>
    /// Builder for constructing an LdAiCompletionConfigDefault instance, which can be passed
    /// as the default value to the AI Client's <see cref="LdAiClient.CompletionConfig"/> method.
    /// </summary>
    public class Builder
    {
        private bool _enabled;
        private readonly List<LdAiConfigTypes.Message> _messages;
        private readonly Dictionary<string, LdValue> _modelParams;
        private readonly Dictionary<string, LdValue> _customModelParams;
        private string _providerName;
        private string _modelName;

        internal Builder()
        {
            _enabled = true;
            _messages = new List<LdAiConfigTypes.Message>();
            _modelParams = new Dictionary<string, LdValue>();
            _customModelParams = new Dictionary<string, LdValue>();
            _providerName = "";
            _modelName = "";
        }

        /// <summary>
        /// Adds a message with the given content and role. The default role is <see cref="LdAiConfigTypes.Role.User"/>.
        /// </summary>
        /// <param name="content">the content, which may contain Mustache templates</param>
        /// <param name="role">the role</param>
        /// <returns>a new builder</returns>
        public Builder AddMessage(string content, LdAiConfigTypes.Role role = LdAiConfigTypes.Role.User)
        {
            _messages.Add(new LdAiConfigTypes.Message(content, role));
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
            var model = new LdAiConfigTypes.ModelConfig(
                _modelName,
                new Dictionary<string, LdValue>(_modelParams),
                new Dictionary<string, LdValue>(_customModelParams));
            var provider = new LdAiConfigTypes.ProviderConfig(_providerName);
            return new LdAiCompletionConfigDefault(_enabled, _messages, model, provider);
        }
    }

    /// <summary>
    /// The prompts associated with the config.
    /// </summary>
    public IReadOnlyList<LdAiConfigTypes.Message> Messages { get; }

    internal LdAiCompletionConfigDefault(bool? enabled, IEnumerable<LdAiConfigTypes.Message> messages,
        LdAiConfigTypes.ModelConfig model, LdAiConfigTypes.ProviderConfig provider)
        : base(enabled, model, provider)
    {
        Messages = messages?.ToList() ?? new List<LdAiConfigTypes.Message>();
    }

    internal LdValue ToLdValue()
    {
        var metaFields = new Dictionary<string, LdValue>
        {
            ["enabled"] = LdValue.Of(Enabled ?? true),
            ["mode"] = LdValue.Of(LdAiCompletionConfig.Mode)
        };

        return LdValue.ObjectFrom(new Dictionary<string, LdValue>
        {
            { "_ldMeta", LdValue.ObjectFrom(metaFields) },
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
            { "provider", LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "name", LdValue.Of(Provider.Name) }
            }) }
        });
    }

    /// <summary>
    /// Creates a new LdAiCompletionConfigDefault builder.
    /// </summary>
    /// <returns>a new builder</returns>
    public static Builder New() => new();

    /// <summary>
    /// Convenient helper that returns a disabled LdAiCompletionConfigDefault.
    /// </summary>
    public static LdAiCompletionConfigDefault Disabled => New().Disable().Build();
}
