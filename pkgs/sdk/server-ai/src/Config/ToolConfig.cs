using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents a tool available to the model in agent mode.
/// </summary>
public sealed record ToolConfig
{
    /// <summary>
    /// The name of the tool.
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// A description of the tool.
    /// </summary>
    public readonly string Description;

    /// <summary>
    /// The type of the tool.
    /// </summary>
    public readonly string Type;

    /// <summary>
    /// The tool's built-in parameters provided by LaunchDarkly.
    /// </summary>
    public readonly IReadOnlyDictionary<string, LdValue> Parameters;

    /// <summary>
    /// The tool's custom parameters provided by the user.
    /// </summary>
    public readonly IReadOnlyDictionary<string, LdValue> CustomParameters;

    internal ToolConfig(
        string name,
        string description,
        string type,
        IReadOnlyDictionary<string, LdValue> parameters,
        IReadOnlyDictionary<string, LdValue> customParameters)
    {
        Name = name;
        Description = description;
        Type = type;
        Parameters = parameters;
        CustomParameters = customParameters;
    }
}
