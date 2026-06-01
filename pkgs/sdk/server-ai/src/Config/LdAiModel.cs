using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Information about the model.
/// </summary>
public sealed record LdAiModel
{
    /// <summary>
    /// The name of the model.
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// The model's built-in parameters provided by LaunchDarkly.
    /// </summary>
    public readonly IReadOnlyDictionary<string, LdValue> Parameters;

    /// <summary>
    /// The model's custom parameters provided by the user.
    /// </summary>
    public readonly IReadOnlyDictionary<string, LdValue> Custom;

    internal LdAiModel(string name, IReadOnlyDictionary<string, LdValue> parameters, IReadOnlyDictionary<string, LdValue> custom)
    {
        Name = name;
        Parameters = parameters;
        Custom = custom;
    }
}
