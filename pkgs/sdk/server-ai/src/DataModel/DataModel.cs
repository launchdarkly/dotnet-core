using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Server.Ai;
using LaunchDarkly.Sdk.Server.Ai.Config;

#pragma warning disable CS1591
namespace  LaunchDarkly.Sdk.Server.Ai.DataModel
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

    public class Meta
    {
        [JsonPropertyName("versionKey")]
        public string VersionKey { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }
    public class Message
    {
        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("role")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Role Role { get; set; }
    }

    public class AiConfig
    {
        [JsonPropertyName("prompt")]
        public List<Message> Prompt { get; set; }

        [JsonPropertyName("_ldMeta")]
        public Meta Meta { get; set; }

        [JsonPropertyName("model")]
        public Dictionary<string, object> Model { get; set; }
    }
}

#pragma warning restore CS1591
