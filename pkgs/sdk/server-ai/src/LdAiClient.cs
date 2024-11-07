using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Mustache;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// The LaunchDarkly AI client. The client is capable of retrieving AI configurations from LaunchDarkly,
/// and generating events specific to usage of the AI configuration when interacting with model providers.
/// </summary>
public sealed class LdAiClient : IDisposable
{
    private readonly ILaunchDarklyClient _client;

    /// <summary>
    /// Constructs a new LaunchDarkly AI client.
    ///
    /// Example:
    /// <code>
    /// var config = Configuration.Builder("my-sdk-key").Build();
    /// var client = new LdClient(config);
    /// var aiClient = new LdAiClient(client);
    /// </code>
    ///
    /// </summary>
    /// <param name="client">a LaunchDarkly Server-side SDK client instance</param>
    public LdAiClient(ILaunchDarklyClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    // This is the special Mustache variable that can be used in prompts to access the current
    // LaunchDarkly context. For example, {{ ldctx.key }} will return the context key.
    private const string LdContextVariable = "ldctx";

    /// <summary>
    /// Retrieves a LaunchDarkly AI config identified by the given key. The return value
    /// is an <see cref="LdAiConfigTracker"/>, which makes the configuration available and
    /// provides convenience methods for generating events related to model usage.
    ///
    /// Any variables provided will be interpolated into the prompt's messages.
    /// Additionally, the current LaunchDarkly context will be available as 'ldctx' within
    /// a prompt message.
    ///
    /// </summary>
    /// <param name="key">the flag key</param>
    /// <param name="context">the context</param>
    /// <param name="defaultValue">the default config, if unable to retrieve from LaunchDarkly</param>
    /// <param name="variables">the list of variables used when interpolating the prompt</param>
    /// <returns>an AI config tracker</returns>
    public LdAiConfigTracker ModelConfig(string key, Context context, LdAiConfig defaultValue,
        IReadOnlyDictionary<string, object> variables = null)
    {

        var detail = _client.JsonVariationDetail(key, context, LdValue.Null);

        var defaultTracker = new LdAiConfigTracker(_client, key, defaultValue, context);

        if (detail.IsDefaultValue)
        {
            _client.GetLogger().Warn("Unable to retrieve AI model config for {0}, returning default config", key);
            return defaultTracker;
        }


        var parsed = ParseConfig(detail.Value, key);
        if (parsed == null)
        {
            // ParseConfig already does logging.
            return defaultTracker;
        }


        var mergedVariables = new Dictionary<string, object> { { LdContextVariable, GetAllAttributes(context) } };
        if (variables != null)
        {
            foreach (var kvp in variables)
            {
                if (kvp.Key == LdContextVariable)
                {
                    _client.GetLogger().Warn("AI model config variables contains 'ldctx' key, which is reserved; this key will be the value of the LaunchDarkly context");
                    continue;
                }
                mergedVariables[kvp.Key] = kvp.Value;
            }
        }


        var prompt =
            parsed.Prompt?.Select(m => new LdAiConfig.Message(InterpolateTemplate(m.Content, mergedVariables), m.Role));

        return new LdAiConfigTracker(_client, key, new LdAiConfig(parsed.Meta?.Enabled ?? false, prompt, parsed.Meta, parsed.Model), context);
    }

    /// <summary>
    /// Retrieves all attributes from the given context, including private attributes. The attributes
    /// are converted into C# primitives recursively.
    /// </summary>
    /// <param name="context">the context</param>
    /// <returns>the attributes</returns>
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

    /// <summary>
    /// Recursively converts an LdValue into a C# object.
    /// </summary>
    /// <param name="value">the LdValue</param>
    /// <returns>the object</returns>
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


    private static string InterpolateTemplate(string template, IReadOnlyDictionary<string, object> variables)
    {
        return Template.Compile(template).Render(variables);
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
            _client.GetLogger().Error("Unable to parse AI model config for key {0}: {1}", key, e.Message);
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
