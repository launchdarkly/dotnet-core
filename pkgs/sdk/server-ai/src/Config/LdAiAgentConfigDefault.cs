using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents a default AI Agent Config supplied by the user as a fallback to
/// the AI client's agent config method. This type contains the same data fields
/// as <see cref="LdAiAgentConfig"/> but has no tracker — it is purely an input
/// to the client.
///
/// Construct an instance via <see cref="New"/> and the nested <see cref="Builder"/>,
/// or use <see cref="Disabled"/> for a disabled default.
/// </summary>
public sealed class LdAiAgentConfigDefault : LdAiConfigDefaultBase
{
    /// <summary>
    /// Builder for constructing an <see cref="LdAiAgentConfigDefault"/> instance.
    /// </summary>
    public class Builder
    {
        private bool _enabled;
        private string _instructions;
        private readonly Dictionary<string, LdValue> _modelParams;
        private readonly Dictionary<string, LdValue> _customModelParams;
        private string _providerName;
        private string _modelName;

        internal Builder()
        {
            _enabled = true;
            _instructions = null;
            _modelParams = new Dictionary<string, LdValue>();
            _customModelParams = new Dictionary<string, LdValue>();
            _providerName = "";
            _modelName = "";
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
        /// Sets the enabled state of the config.
        /// </summary>
        /// <param name="enabled">whether the config is enabled</param>
        /// <returns>the builder</returns>
        public Builder SetEnabled(bool enabled)
        {
            _enabled = enabled;
            return this;
        }

        /// <summary>
        /// Sets the agent's system instructions. May contain Mustache templates.
        /// </summary>
        /// <param name="instructions">the instructions string</param>
        /// <returns>the builder</returns>
        public Builder SetInstructions(string instructions)
        {
            _instructions = instructions;
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
        /// Sets the model's name.
        /// </summary>
        /// <param name="name">the model name</param>
        /// <returns>the builder</returns>
        public Builder SetModelName(string name)
        {
            _modelName = name;
            return this;
        }

        /// <summary>
        /// Sets the model provider's name.
        /// </summary>
        /// <param name="name">the provider name</param>
        /// <returns>the builder</returns>
        public Builder SetModelProviderName(string name)
        {
            _providerName = name;
            return this;
        }

        /// <summary>
        /// Builds the <see cref="LdAiAgentConfigDefault"/> instance.
        /// </summary>
        /// <returns>a new LdAiAgentConfigDefault</returns>
        public LdAiAgentConfigDefault Build()
        {
            var model = new ModelConfig(
                _modelName,
                new Dictionary<string, LdValue>(_modelParams),
                new Dictionary<string, LdValue>(_customModelParams));
            var provider = new ProviderConfig(_providerName);
            return new LdAiAgentConfigDefault(_enabled, _instructions, model, provider);
        }
    }

    /// <summary>
    /// The agent's system instructions, which may contain Mustache templates.
    /// </summary>
    public string Instructions { get; }

    internal LdAiAgentConfigDefault(bool? enabled, string instructions, ModelConfig model, ProviderConfig provider)
        : base(enabled, model, provider)
    {
        Instructions = instructions;
    }

    internal LdValue ToLdValue()
    {
        var metaFields = new Dictionary<string, LdValue>
        {
            ["enabled"] = LdValue.Of(Enabled ?? true),
            ["mode"] = LdValue.Of(LdAiAgentConfig.Mode)
        };

        var root = new Dictionary<string, LdValue>
        {
            { "_ldMeta", LdValue.ObjectFrom(metaFields) },
            { "instructions", LdValue.Of(Instructions) },
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
        };

        return LdValue.ObjectFrom(root);
    }

    /// <summary>
    /// Creates a new <see cref="LdAiAgentConfigDefault"/> builder.
    /// </summary>
    /// <returns>a new builder</returns>
    public static Builder New() => new();

    /// <summary>
    /// Convenient helper that returns a disabled <see cref="LdAiAgentConfigDefault"/>.
    /// </summary>
    public static LdAiAgentConfigDefault Disabled => New().Disable().Build();
}
