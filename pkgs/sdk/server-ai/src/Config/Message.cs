using LaunchDarkly.Sdk.Server.Ai.DataModel;

namespace LaunchDarkly.Sdk.Server.Ai.Config;

/// <summary>
/// Represents a single message, which is part of a prompt.
/// </summary>
public sealed record Message
{
    /// <summary>
    /// The content of the message, which may contain Mustache templates.
    /// </summary>
    public readonly string Content;

    /// <summary>
    /// The role of the message.
    /// </summary>
    public readonly Role Role;

    internal Message(string content, Role role)
    {
        Content = content;
        Role = role;
    }
}
