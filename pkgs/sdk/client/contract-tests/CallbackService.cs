using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Internal.Concurrent;
using LaunchDarkly.TestHelpers.HttpTest;

namespace TestService
{
    public class CallbackService
    {
        // Hook stages are synchronous SDK methods, so they call the synchronous Post.

        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly Uri _uri;

        public CallbackService(Uri uri)
        {
            _uri = uri;
        }

        public void Post(string path, object parameters) =>
            AsyncUtils.WaitSafely(() => PostAsync(path, parameters));

        public async Task PostAsync(string path, object parameters)
        {
            using (var resp = await _httpClient.PostAsync(_uri + path,
                new StringContent(
                    parameters == null ? "{}" : JsonSerializer.Serialize(parameters, SimpleJsonService.SerializerOptions),
                    Encoding.UTF8,
                    "application/json")))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    string body = resp.Content == null ? "" : await resp.Content.ReadAsStringAsync();
                    throw new Exception(string.Format("Callback to {0} returned HTTP error {1} {2}",
                        _uri + path, (int)resp.StatusCode, body));
                }
            }
        }
    }
}
