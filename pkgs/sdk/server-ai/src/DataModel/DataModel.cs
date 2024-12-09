using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace  LaunchDarkly.Sdk.Server.Ai.DataModel;

/// <summary>
/// Represents the role of the prompt message.
/// </summary>
public enum Role
{
    /// <summary>
    /// User role.
    /// </summary>
    User,
    /// <summary>
    /// System role.
    /// </summary>
    System,
    /// <summary>
    /// Assistant role.
    /// </summary>
    Assistant
}

/// <summary>
/// Represents the JSON serialization of the Meta field.
/// </summary>
public class Meta
{
    /// <summary>
    /// The version key.
    /// </summary>
    [JsonPropertyName("versionKey")]
    public string VersionKey { get; set; }

    /// <summary>
    /// If the config is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>
/// Represents the JSON serialization of a Message.
/// </summary>
public class Message
{
    /// <summary>
    /// The content.
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; }

    /// <summary>
    /// The role.
    /// </summary>
    [JsonPropertyName("role")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Role Role { get; set; }
}


/// <summary>
/// Represents the JSON serialization of an AiConfig.
/// </summary>

public class AiConfig
{
    /// <summary>
    /// The prompt.
    /// </summary>
    [JsonPropertyName("messages")]
    public List<Message> Messages { get; set; }

    /// <summary>
    /// LaunchDarkly metadata.
    /// </summary>
    [JsonPropertyName("_ldMeta")]
    public Meta Meta { get; set; }

    /// <summary>
    /// The model configuration.
    /// </summary>
    [JsonPropertyName("model")]
    public Model Model { get; set; }

    /// <summary>
    /// The model provider.
    /// </summary>
    [JsonPropertyName("provider")]
    public Provider Provider { get; set; }
}

/// <summary>
/// Represents the JSON serialization of a model.
/// </summary>
public class Model
{
    /// <summary>
    /// The model's name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>
    /// The model's parameters. These are provided by LaunchDarkly.
    /// </summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, LdValue> Parameters { get; set; }

    /// <summary>
    /// The model's custom parameters. These are arbitrary and provided by the user.
    /// </summary>
    [JsonPropertyName("custom")]
    public Dictionary<string, LdValue> Custom { get; set; }
}

/// <summary>
/// Represents the JSON serialization of a model provider.
/// </summary>
public class Provider
{
    /// <summary>
    ///  The provider's name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }
}
