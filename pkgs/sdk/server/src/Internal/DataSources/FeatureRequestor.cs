using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Server.Subsystems;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    using BytesWithHeaders = Tuple<byte[], IEnumerable<KeyValuePair<string, IEnumerable<string>>>>;

    internal class FeatureRequestor : IFeatureRequestor
    {
        private readonly Uri _allUri;
        private readonly HttpClient _httpClient;
        private readonly HttpProperties _httpProperties;
        private readonly TimeSpan _connectTimeout;
        private readonly Dictionary<Uri, EntityTagHeaderValue> _etags = new Dictionary<Uri, EntityTagHeaderValue>();
        private readonly Logger _log;

        internal FeatureRequestor(LdClientContext context, Uri baseUri)
        {
            _httpProperties = context.Http.HttpProperties;
            _httpClient = context.Http.NewHttpClient();
            _connectTimeout = context.Http.ConnectTimeout;
            _allUri = baseUri.AddPath(StandardEndpoints.PollingRequestPath);
            _log = context.Logger.SubLogger(LogNames.DataSourceSubLog);
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient.Dispose();
            }
        }

        // Returns a data set of the latest flags and segments, or null if they have not been modified. Throws an
        // exception if there was a problem getting data.
        public async Task<DataSetWithHeaders> GetAllDataAsync()
        {
            var res = await GetAsync(_allUri);
            if (res is null)
            {
                return null;
            }
            var data = ParseAllData(res.Item1);
            Func<DataKind, int> countItems = kind =>
                data.Data.FirstOrDefault(kv => kv.Key == kind).Value.Items?.Count() ?? 0;
            _log.Debug("Get all returned {0} feature flags and {1} segments",
                countItems(DataModel.Features), countItems(DataModel.Segments));
            return new DataSetWithHeaders(data, res.Item2);
        }

        private FullDataSet<ItemDescriptor> ParseAllData(byte[] json)
        {
            var r = new Utf8JsonReader(json);
            return StreamProcessorEvents.ParseFullDataset(ref r);
        }

        private async Task<BytesWithHeaders> GetAsync(Uri path)
        {
            _log.Debug("Getting flags with uri: {0}", path.AbsoluteUri);
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            _httpProperties.AddHeaders(request);
            lock (_etags)
            {
                if (_etags.TryGetValue(path, out var etag))
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
                            _log.Debug("Get all flags returned 304: not modified");
                            return null;
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            throw new UnsuccessfulResponseException((int)response.StatusCode);
                        }
                        lock (_etags)
                        {
                            if (response.Headers.ETag != null)
                            {
                                _etags[path] = response.Headers.ETag;
                            }
                            else
                            {
                                _etags.Remove(path);
                            }
                        }
                        var content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        return new BytesWithHeaders(content.Length == 0 ? null : content, response.Headers);
                    }
                }
                catch (TaskCanceledException tce)
                {
                    if (tce.CancellationToken == cts.Token)
                    {
                        //Indicates the task was cancelled by something other than a request timeout
                        throw;
                    }
                    //Otherwise this was a request timeout.
                    throw new TimeoutException("Get item with URL: " + path.AbsoluteUri +
                                                " timed out after : " + _connectTimeout);
                }
            }
        }
    }
}
