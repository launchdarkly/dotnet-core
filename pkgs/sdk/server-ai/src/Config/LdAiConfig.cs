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
    public class LdAiConfig
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
        public class Builder
        {
            private List<Message> _prompt;


            /// <summary>
            /// TBD
            /// </summary>
            public Builder()
            {
                _prompt = new List<Message>();
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="content"></param>
            /// <param name="role"></param>
            /// <returns></returns>
            public Builder AddPromptMessage(string content, Role role = Role.System)
            {
               _prompt.Add(new Message(content, role));
                return this;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns></returns>
            public LdAiConfig Build()
            {
                return new LdAiConfig(_prompt, new Meta(), new Dictionary<string, object>());
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly IReadOnlyList<Message> Prompt;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly IReadOnlyDictionary<string, object> Model;

        private readonly Meta _meta;
        private readonly bool _enabled;

        private LdAiConfig(bool enabled, IEnumerable<Message> prompt, Meta meta, IReadOnlyDictionary<string, object> model)
        {
            Model = model;
            Prompt = prompt?.ToList();
            _meta = meta;
            _enabled = enabled;
        }

        internal LdAiConfig(IEnumerable<Message> prompt, Meta meta,
            IReadOnlyDictionary<string, object> model) : this(true, prompt, meta, model) {}


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns></returns>
        public static Builder New() => new();

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns></returns>
        public bool IsEnabled() => _enabled;

        /// <summary>
        /// TBD
        /// </summary>
        public static LdAiConfig Disabled = new LdAiConfig(false, null, null, null);

    }
}
