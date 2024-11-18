using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using LaunchDarkly.Sdk.Server.Ai.Adapters;
using LaunchDarkly.Sdk.Server.Ai.Config;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Mustache;

namespace LaunchDarkly.Sdk.Server.Ai;

/// <summary>
/// The LaunchDarkly AI client. The client is capable of retrieving AI configurations from LaunchDarkly,
/// and generating events specific to usage of the AI configuration when interacting with model providers.
/// </summary>
public sealed class LdAiClient : ILdAiClient
{
    private readonly ILaunchDarklyClient _client;
    private readonly ILogger _logger;

    /// <summary>
    /// Constructs a new LaunchDarkly AI client. Please note, the client library is an alpha release and is
    /// not considered ready for production use.
    ///
    /// Example:
    /// <code>
    /// var baseClient = new LdClient(Configuration.Builder("my-sdk-key").Build());
    /// var aiClient = new LdAiClient(new LdClientAdapter(baseClient));
    /// </code>
    ///
    /// </summary>
    /// <param name="client">an object satisfying <see cref="ILaunchDarklyClient"/>, such as an <see cref="LdClientAdapter"/></param>
    public LdAiClient(ILaunchDarklyClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = _client.GetLogger();
    }


    // This is the special Mustache variable that can be used in prompts to access the current
    // LaunchDarkly context. For example, {{ ldctx.key }} will return the context key.
    private const string LdContextVariable = "ldctx";

    /// <inheritdoc/>
    public ILdAiConfigTracker ModelConfig(string key, Context context, LdAiConfig defaultValue,
        IReadOnlyDictionary<string, object> variables = null)
    {

        var result = _client.JsonVariation(key, context, defaultValue.ToLdValue());

        var parsed = ParseConfig(result, key);
        if (parsed == null)
        {
            // ParseConfig already does logging.
            return new LdAiConfigTracker(_client, key, defaultValue, context);
        }


        var mergedVariables = new Dictionary<string, object> { { LdContextVariable, GetAllAttributes(context) } };
        if (variables != null)
        {
            foreach (var kvp in variables)
            {
                if (kvp.Key == LdContextVariable)
                {
                    _logger.Warn("AI model config variables contains 'ldctx' key, which is reserved; this key will be the value of the LaunchDarkly context");
                    continue;
                }
                mergedVariables[kvp.Key] = kvp.Value;
            }
        }


        var prompt = new List<LdAiConfig.Message>();

        if (parsed.Prompt != null)
        {
            for (var i = 0; i < parsed.Prompt.Count; i++)
            {
                try
                {
                    var content = InterpolateTemplate(parsed.Prompt[i].Content, mergedVariables);
                    prompt.Add(new LdAiConfig.Message(content, parsed.Prompt[i].Role));
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        $"AI model config prompt has malformed message at index {i}: {ex.Message} (returning default config, which will not contain interpolated prompt messages)");
                    return new LdAiConfigTracker(_client, key, defaultValue, context);
                }
            }
        }

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
            _logger.Error(
                $"Unable to parse AI model config for key {key}: {e.Message} (returning default config, which will not contain interpolated prompt messages)");
            return null;
        }
    }
}
