using System.Collections.Generic;
using System.Linq;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents a default AI Judge Config supplied by the user as a fallback to
/// the AI client's judge config method. This type contains the same data fields
/// as <see cref="LdAiJudgeConfig"/> but has no tracker — it is purely an input
/// to the client.
///
/// Construct an instance via <see cref="New"/> and the nested <see cref="Builder"/>,
/// or use <see cref="Disabled"/> for a disabled default.
/// </summary>
public sealed class LdAiJudgeConfigDefault : LdAiConfigDefaultBase
{
    /// <summary>
    /// Builder for constructing an <see cref="LdAiJudgeConfigDefault"/> instance.
    /// </summary>
    public class Builder
    {
        private bool _enabled;
        private readonly List<Message> _messages;
        private string _evaluationMetricKey;
        private readonly Dictionary<string, LdValue> _modelParams;
        private readonly Dictionary<string, LdValue> _customModelParams;
        private string _providerName;
        private string _modelName;

        internal Builder()
        {
            _enabled = true;
            _messages = new List<Message>();
            _evaluationMetricKey = null;
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
        /// <returns>the builder</returns>
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
        /// Sets the evaluation metric key used to identify this judge's metric.
        /// </summary>
        /// <param name="key">the metric key</param>
        /// <returns>the builder</returns>
        public Builder SetEvaluationMetricKey(string key)
        {
            _evaluationMetricKey = key;
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
        /// Builds the <see cref="LdAiJudgeConfigDefault"/> instance.
        /// </summary>
        /// <returns>a new LdAiJudgeConfigDefault</returns>
        public LdAiJudgeConfigDefault Build()
        {
            var model = new ModelConfig(
                _modelName,
                new Dictionary<string, LdValue>(_modelParams),
                new Dictionary<string, LdValue>(_customModelParams));
            var provider = new ProviderConfig(_providerName);
            return new LdAiJudgeConfigDefault(_enabled, _messages, _evaluationMetricKey, model, provider);
        }
    }

    /// <summary>
    /// The prompts associated with the judge config.
    /// </summary>
    public IReadOnlyList<Message> Messages { get; }

    /// <summary>
    /// The evaluation metric key used to identify this judge's metric.
    /// </summary>
    public string EvaluationMetricKey { get; }

    internal LdAiJudgeConfigDefault(bool? enabled, IEnumerable<Message> messages,
        string evaluationMetricKey, ModelConfig model, ProviderConfig provider)
        : base(enabled, model, provider)
    {
        Messages = messages?.ToList() ?? new List<Message>();
        EvaluationMetricKey = evaluationMetricKey;
    }

    internal LdValue ToLdValue()
    {
        var metaFields = new Dictionary<string, LdValue>
        {
            ["enabled"] = LdValue.Of(Enabled ?? true),
            ["mode"] = LdValue.Of(LdAiJudgeConfig.Mode)
        };

        var root = new Dictionary<string, LdValue>
        {
            { "_ldMeta", LdValue.ObjectFrom(metaFields) },
            { "messages", LdValue.ArrayFrom(Messages.Select(m => LdValue.ObjectFrom(new Dictionary<string, LdValue>
            {
                { "content", LdValue.Of(m.Content) },
                { "role", LdValue.Of(m.Role.ToString()) }
            }))) },
            { "evaluationMetricKey", LdValue.Of(EvaluationMetricKey) },
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
    /// Creates a new <see cref="LdAiJudgeConfigDefault"/> builder.
    /// </summary>
    /// <returns>a new builder</returns>
    public static Builder New() => new();

    /// <summary>
    /// Convenient helper that returns a disabled <see cref="LdAiJudgeConfigDefault"/>.
    /// </summary>
    public static LdAiJudgeConfigDefault Disabled => New().Disable().Build();
}
