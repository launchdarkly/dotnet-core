using System;
using System.Collections.Generic;
using Mustache;

namespace LaunchDarkly.Sdk.Server.Ai
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class LdAiConfigTracker : IDisposable
    {
        private readonly LdClient _client;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="client">an LdClient instance</param>
        public LdAiConfigTracker(LdClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Parses and interpolates a template string with the provided variables.
        /// </summary>
        /// <param name="template">the template string to be parsed and interpolated</param>
        /// <param name="variables">a dictionary containing the variables to be used for interpolation</param>
        /// <returns>the interpolated string</returns>
        public string InterpolateTemplate(string template, IReadOnlyDictionary<string, object> variables)
        {
            return Template.Compile(template).Render(variables);
        }

        /// <summary>
        /// THD
        /// </summary>
        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
