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
    public record LdAiConfig
    {

        /// <summary>
        /// TBD
        /// </summary>
        public record Message
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
            private  List<Message> _prompt;
            private bool _enabled;


            /// <summary>
            /// TBD
            /// </summary>
            public Builder()
            {
                _prompt = new List<Message>();
                _enabled = true;
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
            ///
            /// </summary>
            /// <returns></returns>
            public Builder Disable()
            {
                return SetEnabled(false);
            }

            /// <summary>
            ///
            /// </summary>
            /// <param name="enabled"></param>
            /// <returns></returns>
            public Builder SetEnabled(bool enabled)
            {
                _enabled = enabled;
                return this;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns></returns>
            public LdAiConfig Build()
            {
                return new LdAiConfig(_enabled, _prompt, new Meta(), new Dictionary<string, object>());
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

        private readonly string _versionKey;
        private readonly bool _enabled;

        internal LdAiConfig(bool enabled, IEnumerable<Message> prompt, Meta meta, IReadOnlyDictionary<string, object> model)
        {
            Model = model;
            Prompt = prompt?.ToList();
            _versionKey = meta?.VersionKey ?? "";
            _enabled = enabled;
        }


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
