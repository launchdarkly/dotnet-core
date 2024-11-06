using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
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
            private readonly List<Message> _prompt;
            private bool _enabled;
            private Dictionary<string, object> _modelParams;


            /// <summary>
            /// TBD
            /// </summary>
            public Builder()
            {
                _enabled = true;
                _prompt = new List<Message>();
                _modelParams = new Dictionary<string, object>();
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="content"></param>
            /// <param name="role"></param>
            /// <returns></returns>
            public Builder AddPromptMessage(string content, Role role = Role.User)
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
            ///
            /// </summary>
            /// <param name="key"></param>
            /// <param name="value"></param>
            /// <returns></returns>
            public Builder SetModelParam(string key, object value)
            {
                _modelParams[key] = value;
                return this;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns></returns>
            public LdAiConfig Build()
            {
                return new LdAiConfig(_enabled, _prompt, new Meta(), _modelParams);
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
        ///
        /// </summary>
        public string VersionKey => _versionKey;

        /// <summary>
        /// TBD
        /// </summary>
        public static LdAiConfig Disabled = new LdAiConfig(false, null, null, null);

    }
}
