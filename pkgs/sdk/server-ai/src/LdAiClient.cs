using System;
using LaunchDarkly.Sdk.Server;

namespace LaunchDarkly.Sdk.Server.Ai
{
    class LdAiClient : IDisposable
    {
        private readonly LdClient _client;

        public LdAiClient(LdClient client)
        {
            _client = client;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
