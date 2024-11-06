using System;

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
        /// THD
        /// </summary>
        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
