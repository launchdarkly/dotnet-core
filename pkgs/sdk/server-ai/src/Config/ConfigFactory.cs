using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.Interfaces;
using Mustache;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Owns the translation from an evaluated <see cref="LdValue"/> to a typed AI Config.
/// Holds the underlying LaunchDarkly client and logger so each per-mode build method
/// can synthesize a tracker factory and merge in context-derived prompt variables
/// without the public <see cref="LdAiClient"/> having to know any of those details.
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
        LdAiCompletionConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> variables)
    {
        var mergedVars = MergeVariables(variables, context);
        var trackerFactory = TrackerFactoryFor(context);

        if (ldValue.Type != LdValueType.Object)
        {
            _logger.Error(
                "AI Config '{0}': variation result is not an object (got {1}); using caller's default.",
                key, ldValue.Type);
            return BuildCompletionFromDefault(key, defaultValue, mergedVars, trackerFactory);
        }

        var (enabled, variationKey, version, mode) = ParseMeta(ldValue);

        if (mode != LdAiCompletionConfig.Mode)
        {
            _logger.Warn(
                "AI Config mode mismatch for {0}: expected {1}, got {2}. Returning caller's default.",
                key, LdAiCompletionConfig.Mode, mode);
            return BuildCompletionFromDefault(key, defaultValue, mergedVars, trackerFactory);
        }

        var model = ParseModel(ldValue.Get("model"));
        var provider = ParseProvider(ldValue.Get("provider"));
        var messages = InterpolateMessages(ParseMessages(ldValue.Get("messages")), mergedVars, key);

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

    private LdAiCompletionConfig BuildCompletionFromDefault(
        string key,
        LdAiCompletionConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> mergedVars,
        Func<LdAiConfig, ILdAiConfigTracker> trackerFactory)
    {
        // Caller-supplied default messages can contain Mustache templates too; interpolate
        // with the same per-message fallback as server-returned configs.
        var messages = InterpolateMessages(defaultValue.Messages, mergedVars, key);
        return new LdAiCompletionConfig(
            key,
            defaultValue.Enabled ?? true,
            variationKey: "",
            version: 1,
            messages,
            defaultValue.Model,
            defaultValue.Provider,
            trackerFactory);
    }

    public LdAiAgentConfig BuildAgentConfig(
        string key,
        LdValue ldValue,
        Context context,
        LdAiAgentConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> variables)
    {
        var mergedVars = MergeVariables(variables, context);
        var trackerFactory = TrackerFactoryFor(context);

        if (ldValue.Type != LdValueType.Object)
        {
            _logger.Error(
                "AI Config '{0}': variation result is not an object (got {1}); using caller's default.",
                key, ldValue.Type);
            return BuildAgentFromDefault(key, defaultValue, mergedVars, trackerFactory);
        }

        var (enabled, variationKey, version, mode) = ParseMeta(ldValue);

        if (mode != LdAiAgentConfig.Mode)
        {
            _logger.Warn(
                "AI Config mode mismatch for {0}: expected {1}, got {2}. Returning caller's default.",
                key, LdAiAgentConfig.Mode, mode);
            return BuildAgentFromDefault(key, defaultValue, mergedVars, trackerFactory);
        }

        var model = ParseModel(ldValue.Get("model"));
        var provider = ParseProvider(ldValue.Get("provider"));
        var tools = ParseTools(ldValue.Get("tools"));
        var instructions = InterpolateInstructions(ParseInstructions(ldValue), mergedVars, key);

        return new LdAiAgentConfig(
            key,
            enabled,
            variationKey,
            version,
            instructions,
            tools,
            model,
            provider,
            trackerFactory);
    }

    private LdAiAgentConfig BuildAgentFromDefault(
        string key,
        LdAiAgentConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> mergedVars,
        Func<LdAiConfig, ILdAiConfigTracker> trackerFactory)
    {
        var instructions = InterpolateInstructions(defaultValue.Instructions, mergedVars, key);
        return new LdAiAgentConfig(
            key,
            defaultValue.Enabled ?? true,
            variationKey: "",
            version: 1,
            instructions,
            tools: new Dictionary<string, ToolConfig>(),
            defaultValue.Model,
            defaultValue.Provider,
            trackerFactory);
    }

    public LdAiJudgeConfig BuildJudgeConfig(
        string key,
        LdValue ldValue,
        Context context,
        LdAiJudgeConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> variables)
    {
        var mergedVars = MergeVariables(variables, context);
        var trackerFactory = TrackerFactoryFor(context);

        if (ldValue.Type != LdValueType.Object)
        {
            _logger.Error(
                "AI Config '{0}': variation result is not an object (got {1}); using caller's default.",
                key, ldValue.Type);
            return BuildJudgeFromDefault(key, defaultValue, mergedVars, trackerFactory);
        }

        var (enabled, variationKey, version, mode) = ParseMeta(ldValue);

        if (mode != LdAiJudgeConfig.Mode)
        {
            _logger.Warn(
                "AI Config mode mismatch for {0}: expected {1}, got {2}. Returning caller's default.",
                key, LdAiJudgeConfig.Mode, mode);
            return BuildJudgeFromDefault(key, defaultValue, mergedVars, trackerFactory);
        }

        var model = ParseModel(ldValue.Get("model"));
        var provider = ParseProvider(ldValue.Get("provider"));
        var messages = InterpolateMessages(ParseMessages(ldValue.Get("messages")), mergedVars, key);
        var evaluationMetricKey = ParseEvaluationMetricKey(ldValue);

        return new LdAiJudgeConfig(
            key,
            enabled,
            variationKey,
            version,
            messages,
            evaluationMetricKey,
            model,
            provider,
            trackerFactory);
    }

    private LdAiJudgeConfig BuildJudgeFromDefault(
        string key,
        LdAiJudgeConfigDefault defaultValue,
        IReadOnlyDictionary<string, object> mergedVars,
        Func<LdAiConfig, ILdAiConfigTracker> trackerFactory)
    {
        var messages = InterpolateMessages(defaultValue.Messages, mergedVars, key);
        return new LdAiJudgeConfig(
            key,
            defaultValue.Enabled ?? true,
            variationKey: "",
            version: 1,
            messages,
            defaultValue.EvaluationMetricKey,
            defaultValue.Model,
            defaultValue.Provider,
            trackerFactory);
    }

    private string InterpolateInstructions(
        string instructions,
        IReadOnlyDictionary<string, object> mergedVars,
        string key)
    {
        if (instructions == null) return null;
        try
        {
            return InterpolateTemplate(instructions, mergedVars);
        }
        catch (Exception ex)
        {
            _logger.Warn(
                $"AI Config '{key}': skipping interpolation of malformed instructions template: {ex.Message}");
            return instructions;
        }
    }

    private IReadOnlyList<LdAiConfigTypes.Message> InterpolateMessages(
        IReadOnlyList<LdAiConfigTypes.Message> messages,
        IReadOnlyDictionary<string, object> mergedVars,
        string key)
    {
        if (messages == null)
        {
            return new List<LdAiConfigTypes.Message>();
        }
        var result = new List<LdAiConfigTypes.Message>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            string interpolated;
            try
            {
                interpolated = InterpolateTemplate(msg.Content, mergedVars);
            }
            catch (Exception ex)
            {
                _logger.Warn(
                    $"AI Config '{key}': skipping interpolation of malformed template in message {i}: {ex.Message}");
                interpolated = msg.Content;
            }
            result.Add(new LdAiConfigTypes.Message(interpolated, msg.Role));
        }
        return result;
    }

    private Func<LdAiConfig, ILdAiConfigTracker> TrackerFactoryFor(Context context)
    {
        return cfg => new LdAiConfigTracker(
            _client,
            Guid.NewGuid().ToString(),
            cfg.Key,
            cfg.VariationKey,
            cfg.Version,
            context,
            cfg.Model?.Name,
            cfg.Provider?.Name);
    }

    private static (bool Enabled, string VariationKey, int Version, string Mode) ParseMeta(LdValue value)
    {
        var meta = value.Get("_ldMeta");
        var enabled = meta.Get("enabled").AsBool;
        var variationKey = meta.Get("variationKey").AsString ?? "";
        var versionValue = meta.Get("version");
        var version = versionValue.IsNull ? 1 : versionValue.AsInt;
        // Default to the completion mode when _ldMeta.mode is missing or non-string: legacy
        // flags predate the mode tag and were always served as completion configs.
        var mode = meta.Get("mode").AsString ?? LdAiCompletionConfig.Mode;
        return (enabled, variationKey, version, mode);
    }

    private static LdAiConfigTypes.ModelConfig ParseModel(LdValue modelValue)
    {
        var name = modelValue.Get("name").AsString ?? "";
        var parameters = LdValueObjectToDictionary(modelValue.Get("parameters"));
        var custom = LdValueObjectToDictionary(modelValue.Get("custom"));
        return new LdAiConfigTypes.ModelConfig(name, parameters, custom);
    }

    private static LdAiConfigTypes.ProviderConfig ParseProvider(LdValue providerValue)
    {
        var name = providerValue.Get("name").AsString ?? "";
        return new LdAiConfigTypes.ProviderConfig(name);
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

    private static IReadOnlyList<LdAiConfigTypes.Message> ParseMessages(LdValue messagesValue)
    {
        if (messagesValue.Type != LdValueType.Array)
        {
            return new List<LdAiConfigTypes.Message>();
        }

        var result = new List<LdAiConfigTypes.Message>(messagesValue.Count);
        for (var i = 0; i < messagesValue.Count; i++)
        {
            var msg = messagesValue.Get(i);
            if (msg.Type != LdValueType.Object)
            {
                continue;
            }
            var content = msg.Get("content").AsString ?? "";
            var role = ParseRole(msg.Get("role").AsString);
            result.Add(new LdAiConfigTypes.Message(content, role));
        }
        return result;
    }

    internal static string ParseInstructions(LdValue value)
    {
        var instructions = value.Get("instructions");
        return instructions.Type == LdValueType.String ? instructions.AsString : null;
    }

    internal static IReadOnlyDictionary<string, LdAiConfigTypes.Tool> ParseTools(LdValue toolsValue)
    {
        if (toolsValue.Type != LdValueType.Object) return ImmutableDictionary<string, LdAiConfigTypes.Tool>.Empty;
        var result = ImmutableDictionary.CreateBuilder<string, LdAiConfigTypes.Tool>();
        foreach (var kv in toolsValue.Dictionary)
        {
            var tool = kv.Value;
            if (tool.Type != LdValueType.Object) continue;
            result[kv.Key] = new LdAiConfigTypes.Tool(
                tool.Get("name").AsString ?? "",
                tool.Get("description").AsString,
                tool.Get("type").AsString,
                LdValueObjectToDictionary(tool.Get("parameters")),
                LdValueObjectToDictionary(tool.Get("customParameters")));
        }
        return result.ToImmutable();
    }

    internal static LdAiConfigTypes.JudgeConfiguration ParseJudgeConfiguration(LdValue value)
    {
        var jc = value.Get("judgeConfiguration");
        if (jc.Type != LdValueType.Object) return null;
        var judgesArray = jc.Get("judges");
        if (judgesArray.Type != LdValueType.Array) return new LdAiConfigTypes.JudgeConfiguration(new List<LdAiConfigTypes.Judge>());
        var entries = new List<LdAiConfigTypes.Judge>();
        for (var i = 0; i < judgesArray.Count; i++)
        {
            var j = judgesArray.Get(i);
            if (j.Type != LdValueType.Object) continue;
            entries.Add(new LdAiConfigTypes.Judge(
                j.Get("key").AsString ?? "",
                j.Get("samplingRate").AsDouble));
        }
        return new LdAiConfigTypes.JudgeConfiguration(entries);
    }

    internal static string ParseEvaluationMetricKey(LdValue value)
    {
        var emk = value.Get("evaluationMetricKey");
        return emk.Type == LdValueType.String ? emk.AsString : null;
    }

    private static LdAiConfigTypes.Role ParseRole(string roleString)
    {
        // The wire format uses capitalized "User" / "System" / "Assistant"; Enum.TryParse with
        // ignoreCase = true is tolerant of casing variants. Unknown / null roles fall back to User.
        if (!string.IsNullOrEmpty(roleString) && Enum.TryParse<LdAiConfigTypes.Role>(roleString, ignoreCase: true, out var parsed))
        {
            return parsed;
        }
        return LdAiConfigTypes.Role.User;
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
