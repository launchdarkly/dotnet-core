using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using Mustache;
using Message = LaunchDarkly.Sdk.Server.Ai.Config.Message;

namespace LaunchDarkly.Sdk.Server.Ai
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface ILogger
    {

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="format"></param>
        /// <param name="allParams"></param>
        void Error(string format, params object[] allParams);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="format"></param>
        /// <param name="allParams"></param>
        void Warn(string format, params object[] allParams);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface ILaunchDarklyClient
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="key"></param>
        /// <param name="context"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        EvaluationDetail<LdValue> JsonVariationDetail(string key, Context context, LdValue defaultValue);

        /// <summary>
        /// TBD
        /// </summary>
        void Dispose();

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns></returns>
        ILogger GetLogger();

    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class LdAiClient : IDisposable
    {
        private readonly ILaunchDarklyClient _client;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="client">an ILaunchDarklyClient</param>
        public LdAiClient(ILaunchDarklyClient client)
        {
            _client = client;
        }

        private static string InterpolateTemplate(string template, IReadOnlyDictionary<string, object> variables)
        {
            return Template.Compile(template).Render(variables);
        }

        private static IDictionary<string, object> GetAllAttributes(Context context)
        {
            var attributes = new Dictionary<string, object>();
            foreach (var key in context.OptionalAttributeNames)
            {
                attributes[key] = context.GetValue(AttributeRef.FromLiteral(key));
            }

            attributes["kind"] = context.Kind.ToString();
            attributes["key"] = context.Key;
            attributes["anonymous"] = context.Anonymous;

            return attributes;
        }




        private const string LdContextVariable = "ldctx";

        /// <summary>
        /// Retrieves a LaunchDarkly AI config named by the given key.
        /// </summary>
        /// <param name="key">the flag key</param>
        /// <param name="context">the Context</param>
        /// <param name="defaultValue">the default config, if unable to retrieve</param>
        /// <param name="variables">the list of variables used when interpolating the prompt</param>
        /// <returns>an AI config</returns>
        public LdAiConfig GetModelConfig(string key, Context context, LdAiConfig defaultValue,
            IReadOnlyDictionary<string, object> variables = null)
        {

            // TODO: validate that client is not null?

            var detail = _client.JsonVariationDetail(key, context, LdValue.Null);


            if (detail.IsDefaultValue)
            {
                _client.GetLogger().Warn("No model config available for key {0}", key);
                return defaultValue;
            }


            var parsed = ParseConfig(detail.Value, key);
            if (parsed == null)
            {
                return defaultValue;
            }

            if (parsed.LdMeta is not { Enabled: true })
            {
                return LdAiConfig.Disabled;
            }

            var mergedVariables = new Dictionary<string, object> { { LdContextVariable, GetAllAttributes(context) } };
            if (variables != null)
            {
                foreach (var kvp in variables)
                {
                    if (kvp.Key == LdContextVariable)
                    {
                        _client.GetLogger().Warn("Model config variables contains 'ldctx' key, which is reserved; this key will be the value of the LaunchDarkly context");
                        continue;
                    }
                    mergedVariables[kvp.Key] = kvp.Value;
                }
            }


            var prompt =
                parsed.Prompt?.Select(m => new Message(InterpolateTemplate(m.Content, mergedVariables), m.Role));

            return new LdAiConfig(this, prompt, parsed.LdMeta, parsed.Model);
        }



        private AiConfig ParseConfig(LdValue value, string key)
        {

            var serialized = value.ToJsonString();
            try
            {
                return JsonSerializer.Deserialize<AiConfig>(serialized);
            }
            catch (JsonException e)
            {
                _client.GetLogger().Error("Unable to parse model config for key {0}: {1}", key, e.Message);
                return null;
            }
        }

        /// <summary>
        /// THD
        /// </summary>
        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
