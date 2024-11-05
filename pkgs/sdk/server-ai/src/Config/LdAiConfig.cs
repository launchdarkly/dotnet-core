using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LaunchDarkly.Sdk.Server.Ai.DataModel;

namespace LaunchDarkly.Sdk.Server.Ai.Config
{
    /// <summary>
    /// TBD
    /// </summary>
    public struct Message
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Content;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Role Role;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="content">TBD</param>
        /// <param name="role">TBD</param>
        public Message(string content, Role role)
        {
            Content = content;
            Role = role;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class LdAiConfig
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IReadOnlyList<Message> Prompt;

        /// <summary>
        /// TBD
        /// </summary>
        public  readonly LdAiClient Tracker;
        private readonly Meta _meta;
        private readonly IReadOnlyDictionary<string, object> _model;
        private readonly bool _enabled;

        private LdAiConfig(bool enabled, LdAiClient tracker,  IEnumerable<Message> prompt, Meta meta, IReadOnlyDictionary<string, object> model)
        {
            Tracker = tracker;
            Prompt = prompt?.ToList();
            _meta = meta;
            _model = model;
            _enabled = enabled;
        }

        internal LdAiConfig(LdAiClient tracker, IEnumerable<Message> prompt, Meta meta,
            IReadOnlyDictionary<string, object> model) : this(true, tracker, prompt, meta, model) {}


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns></returns>
        public bool IsEnabled() => _enabled;

        /// <summary>
        /// TBD
        /// </summary>
        public static LdAiConfig Disabled = new LdAiConfig(false, null, null, null, null);

        /// <summary>
        /// TBD
        /// </summary>
        public static LdAiConfig Default = Disabled;
    }
}
