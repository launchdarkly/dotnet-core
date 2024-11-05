using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LaunchDarkly.Sdk.Serve.Ai.DataModel;

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
        public  readonly LdAiConfigTracker Tracker;
        private readonly Meta _meta;
        private readonly IReadOnlyDictionary<string, object> _model;

        internal LdAiConfig(LdAiConfigTracker tracker, IEnumerable<Message> prompt, Meta meta, IReadOnlyDictionary<string, object> model)
        {
            Tracker = tracker;
            Prompt = prompt.ToList();
            _meta = meta;
            _model = model;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static LdAiConfig Default = new LdAiConfig(null, Array.Empty<Message>(), new Meta(), new Dictionary<string, object>());
    }
}
