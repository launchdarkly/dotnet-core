using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    internal sealed class FDv2PollingRequestor : IFDv2PollingRequestor
    {
        private const string VersionQueryParam = "version";
        private const string StateQueryParam = "state";

        private readonly Uri _baseUri;
        private readonly HttpClient _httpClient;
        private readonly HttpProperties _httpProperties;
        private readonly TimeSpan _connectTimeout;
        private readonly Logger _log;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Dictionary<Uri, EntityTagHeaderValue> _etags = new Dictionary<Uri, EntityTagHeaderValue>();

        internal FDv2PollingRequestor(LdClientContext context, Uri baseUri)
        {
            _baseUri = baseUri;
            _httpProperties = context.Http.HttpProperties;
            _httpClient = context.Http.NewHttpClient();
            _connectTimeout = context.Http.ConnectTimeout;
            _log = context.Logger.SubLogger(LogNames.FDv2DataSourceSubLog);

            // Set up JSON deserialization options
            _jsonOptions = new JsonSerializerOptions();
            _jsonOptions.Converters.Add(ServerIntentConverter.Instance);
            _jsonOptions.Converters.Add(PutObjectConverter.Instance);
            _jsonOptions.Converters.Add(DeleteObjectConverter.Instance);
            _jsonOptions.Converters.Add(PayloadTransferredConverter.Instance);
            _jsonOptions.Converters.Add(ErrorConverter.Instance);
            _jsonOptions.Converters.Add(GoodbyeConverter.Instance);
            _jsonOptions.Converters.Add(FDv2PollEventConverter.Instance);
            _jsonOptions.Converters.Add(FeatureFlagSerialization.Instance);
            _jsonOptions.Converters.Add(SegmentSerialization.Instance);
        }

        public async Task<FDv2PollingResponse?> PollingRequestAsync(Selector selector)
        {
            var uri = _baseUri.AddPath(StandardEndpoints.FDv2PollingRequestPath);

            // Add selector query parameters
            var uriBuilder = new UriBuilder(uri);
            var query = new List<string>();

            if (selector.Version > 0)
            {
                query.Add($"{VersionQueryParam}={selector.Version}");
            }

            if (!string.IsNullOrEmpty(selector.State))
            {
                query.Add($"{StateQueryParam}={Uri.EscapeDataString(selector.State)}");
            }

            if (query.Count > 0)
            {
                uriBuilder.Query = string.Join("&", query);
            }

            var requestUri = uriBuilder.Uri;

            _log.Debug("Making FDv2 polling request to {0}", requestUri);

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            // ReSharper disable once PossiblyImpureMethodCallOnReadonlyVariable
            // This method adds the headers to the request, versus adding headers to the properties. Resharper
            // analysis incorrectly thinks it is an impure function.
            _httpProperties.AddHeaders(request);

            lock (_etags)
            {
                if (_etags.TryGetValue(requestUri, out var etag))
                {
                    request.Headers.IfNoneMatch.Add(etag);
                }
            }

            using (var cts = new CancellationTokenSource(_connectTimeout))
            {
                try
                {
                    using (var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotModified)
                        {
                            _log.Debug("FDv2 polling request returned 304: not modified");
                            return null;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            throw new UnsuccessfulResponseException((int)response.StatusCode, response.Headers);
                        }

                        lock (_etags)
                        {
                            if (response.Headers.ETag != null)
                            {
                                _etags[requestUri] = response.Headers.ETag;
                            }
                            else
                            {
                                _etags.Remove(requestUri);
                            }
                        }

                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        _log.Debug("Received FDv2 polling response");

                        // Parse the response which contains an "events" array
                        var events = FDv2Event.DeserializeEventsArray(content, _jsonOptions);

                        var headers = response.Headers
                            .Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value))
                            .ToList();

                        return new FDv2PollingResponse(events, headers);
                    }
                }
                catch (TaskCanceledException tce)
                {
                    if (tce.CancellationToken == cts.Token)
                    {
                        // Indicates the task was canceled by something other than a request timeout.
                        throw;
                    }

                    throw new TimeoutException("FDv2 polling request with URL: " + requestUri.AbsoluteUri +
                                               " timed out after: " + _connectTimeout);
                }
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
