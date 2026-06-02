using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.DataModel;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Mustache;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Owns the translation from an evaluated <see cref="LdValue"/> to a typed AI Config.
/// Holds the underlying LaunchDarkly client and logger so each per-mode build method
/// (currently only <see cref="BuildCompletionConfig"/>; future agent and judge builders will
/// follow the same pattern) can synthesize a tracker factory and merge in
/// context-derived prompt variables without the public <see cref="LdAiClient"/>
/// having to know any of those details.
/// </summary>
internal sealed class ConfigFactory
{
    // This is the special Mustache variable that can be used in prompts to access the current
    // LaunchDarkly context. For example, {{ ldctx.key }} will return the context key.
    private const string LdContextVariable = "ldctx";

    private readonly ILaunchDarklyClient _client;
    private readonly ILogger _logger;

    public ConfigFactory(ILaunchDarklyClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    public LdAiCompletionConfig BuildCompletionConfig(
        string key,
        LdValue ldValue,
        Context context,
        IReadOnlyDictionary<string, object> variables)
    {
        var mergedVars = MergeVariables(variables, context);
        var trackerFactory = TrackerFactoryFor(context);

        var (enabled, variationKey, version, mode) = ParseMeta(ldValue);

        if (mode != "completion")
        {
            _logger.Warn(
                $"AI Config mode mismatch for {key}: expected completion, got {mode}. Returning disabled config.");
            return BuildDisabledCompletionConfig(key, variationKey, version, trackerFactory);
        }

        var model = ParseModel(ldValue.Get("model"));
        var provider = ParseProvider(ldValue.Get("provider"));
        var messages = ParseAndInterpolateMessages(ldValue.Get("messages"), mergedVars, key);

        return new LdAiCompletionConfig(
            key,
            enabled,
            variationKey,
            version,
            messages,
            model,
            provider,
            trackerFactory);
    }

    private static LdAiCompletionConfig BuildDisabledCompletionConfig(
        string key,
        string variationKey,
        int version,
        Func<LdAiConfigBase, ILdAiConfigTracker> trackerFactory)
    {
        return new LdAiCompletionConfig(
            key,
            enabled: false,
            variationKey: variationKey,
            version: version,
            messages: new List<Message>(),
            model: new ModelConfig("", new Dictionary<string, LdValue>(), new Dictionary<string, LdValue>()),
            provider: new ProviderConfig(""),
            trackerFactory: trackerFactory);
    }

    private Func<LdAiConfigBase, ILdAiConfigTracker> TrackerFactoryFor(Context context)
    {
        return cfg => new LdAiConfigTracker(_client, cfg, context);
    }

    private static (bool Enabled, string VariationKey, int Version, string Mode) ParseMeta(LdValue value)
    {
        var meta = value.Get("_ldMeta");
        var enabled = meta.Get("enabled").AsBool;
        var variationKey = meta.Get("variationKey").AsString ?? "";
        var version = meta.Get("version").AsInt;
        // Default to "completion" when _ldMeta.mode is missing or non-string: legacy flags
        // predate the mode tag, so treating them as completion configs matches the existing
        // shape on the wire.
        var mode = meta.Get("mode").AsString ?? "completion";
        return (enabled, variationKey, version, mode);
    }

    private static ModelConfig ParseModel(LdValue modelValue)
    {
        var name = modelValue.Get("name").AsString ?? "";
        var parameters = LdValueObjectToDictionary(modelValue.Get("parameters"));
        var custom = LdValueObjectToDictionary(modelValue.Get("custom"));
        return new ModelConfig(name, parameters, custom);
    }

    private static ProviderConfig ParseProvider(LdValue providerValue)
    {
        var name = providerValue.Get("name").AsString ?? "";
        return new ProviderConfig(name);
    }

    private static IReadOnlyDictionary<string, LdValue> LdValueObjectToDictionary(LdValue value)
    {
        if (value.Type != LdValueType.Object)
        {
            return new Dictionary<string, LdValue>();
        }

        // Materialize into a plain Dictionary so the ModelConfig field has a stable type.
        return value.Dictionary.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private IReadOnlyList<Message> ParseAndInterpolateMessages(
        LdValue messagesValue,
        IReadOnlyDictionary<string, object> mergedVars,
        string key)
    {
        if (messagesValue.Type != LdValueType.Array)
        {
            return new List<Message>();
        }

        var result = new List<Message>(messagesValue.Count);
        for (var i = 0; i < messagesValue.Count; i++)
        {
            var msg = messagesValue.Get(i);
            if (msg.Type != LdValueType.Object)
            {
                continue;
            }

            var content = msg.Get("content").AsString ?? "";
            var role = ParseRole(msg.Get("role").AsString);

            string interpolated;
            try
            {
                interpolated = InterpolateTemplate(content, mergedVars);
            }
            catch (Exception ex)
            {
                _logger.Warn(
                    $"AI Config '{key}': skipping interpolation of malformed template in message {i}: {ex.Message}");
                interpolated = content;
            }

            result.Add(new Message(interpolated, role));
        }

        return result;
    }

    private static Role ParseRole(string roleString)
    {
        // The wire format uses capitalized "User" / "System" / "Assistant"; Enum.TryParse with
        // ignoreCase = true is tolerant of casing variants. Unknown / null roles fall back to User.
        if (!string.IsNullOrEmpty(roleString) && Enum.TryParse<Role>(roleString, ignoreCase: true, out var parsed))
        {
            return parsed;
        }
        return Role.User;
    }

    private IReadOnlyDictionary<string, object> MergeVariables(
        IReadOnlyDictionary<string, object> userVariables, Context context)
    {
        // Seed with user variables; the special "ldctx" key is reserved for the LaunchDarkly
        // context and always overrides any user-supplied value of that key.
        var merged = new Dictionary<string, object>();
        if (userVariables != null)
        {
            foreach (var kvp in userVariables)
            {
                if (kvp.Key == LdContextVariable)
                {
                    _logger.Warn(
                        "AI model config variables contains 'ldctx' key, which is reserved; this key will be the value of the LaunchDarkly context");
                    continue;
                }
                merged[kvp.Key] = kvp.Value;
            }
        }
        merged[LdContextVariable] = GetAllAttributes(context);
        return merged;
    }

    private static string InterpolateTemplate(string template, IReadOnlyDictionary<string, object> variables)
    {
        return Template.Compile(template).Render(variables);
    }

    private static IDictionary<string, object> AddSingleKindContextAttributes(Context context)
    {
        var attributes = new Dictionary<string, object>
        {
            ["kind"] = context.Kind.ToString(),
            ["key"] = context.Key,
            ["anonymous"] = context.Anonymous
        };

        foreach (var key in context.OptionalAttributeNames)
        {
            attributes[key] = ValueToObject(context.GetValue(AttributeRef.FromLiteral(key)));
        }

        return attributes;
    }

    /// <summary>
    /// Retrieves all attributes from the given context, including private attributes. The attributes
    /// are converted into C# primitives recursively.
    /// </summary>
    private static IDictionary<string, object> GetAllAttributes(Context context)
    {
        if (!context.Multiple)
        {
            return AddSingleKindContextAttributes(context);
        }

        var attrs = new Dictionary<string, object>
        {
            ["kind"] = context.Kind,
            ["key"] = context.FullyQualifiedKey
        };

        foreach (var kind in context.MultiKindContexts)
        {
            attrs[kind.Kind.ToString()] = AddSingleKindContextAttributes(kind);
        }

        return attrs;
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
}
