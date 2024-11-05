using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Server.Ai;
using LaunchDarkly.Sdk.Server.Ai.Config;

namespace  LaunchDarkly.Sdk.Serve.Ai.DataModel
{
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

    internal class Meta
    {
        [JsonPropertyName("versionId")]
        internal int VersionId { get; set; }
    }
    internal class Message
    {
        [JsonPropertyName("content")]
        internal string Content { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        internal Role Role { get; set; }
    }

    internal class AiConfig
    {
        [JsonPropertyName("prompt")]
        internal List<Message> Prompt { get; set; }

        [JsonPropertyName("_ldMeta")]
        internal Meta LdMeta { get; set; }

        [JsonPropertyName("model")]
        internal Dictionary<string, object> Model { get; set; }
    }
}
