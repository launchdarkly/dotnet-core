using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using Mustache;

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
            _client = client ?? throw new ArgumentNullException(nameof(client));
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
                attributes[key] = ValueToObject(context.GetValue(AttributeRef.FromLiteral(key)));
            }

            attributes["kind"] = context.Kind.ToString();
            attributes["key"] = context.Key;
            attributes["anonymous"] = context.Anonymous;

            return attributes;
        }

        private static object ValueToObject(LdValue value)
        {
            return value.Type switch
            {
                LdValueType.Null => null,
                LdValueType.Bool => value.AsBool,
                LdValueType.Number => value.AsDouble,
                LdValueType.String => value.AsString,
                LdValueType.Array => value.List.Select(ValueToObject).ToList(),
                LdValueType.Object => value.Dictionary
                    .Select(kv => new KeyValuePair<string, object>(kv.Key, ValueToObject(kv.Value)))
                    .ToImmutableDictionary(),
                _ => null
            };
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
        public LdAiConfigTracker GetModelConfig(string key, Context context, LdAiConfig defaultValue,
            IReadOnlyDictionary<string, object> variables = null)
        {

            var detail = _client.JsonVariationDetail(key, context, LdValue.Null);

            if (detail.IsDefaultValue)
            {
                _client.GetLogger().Warn("No model config available for key {0}", key);
                return new LdAiConfigTracker(_client, defaultValue);
            }


            var parsed = ParseConfig(detail.Value, key);
            if (parsed == null)
            {
                // ParseConfig already does logging.
                return new LdAiConfigTracker(_client, defaultValue);
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
                parsed.Prompt?.Select(m => new LdAiConfig.Message(InterpolateTemplate(m.Content, mergedVariables), m.Role));

            return new LdAiConfigTracker(_client, new LdAiConfig(prompt, parsed.Meta, parsed.Model));
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
