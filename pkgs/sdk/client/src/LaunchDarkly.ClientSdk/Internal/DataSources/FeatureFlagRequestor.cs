using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Internal.Http;
using LaunchDarkly.Sdk.Client.Subsystems;

namespace LaunchDarkly.Sdk.Client.Internal.DataSources
{
    internal struct WebResponse
    {
        public int statusCode { get; private set; }
        public string jsonResponse { get; private set; }
        public string errorMessage { get; private set; }

        public WebResponse(int code, string response, string error)
        {
            statusCode = code;
            jsonResponse = response;
            errorMessage = error;
        }
    }

    internal interface IFeatureFlagRequestor : IDisposable
    {
        Task<WebResponse> FeatureFlagsAsync();
    }

    internal sealed class FeatureFlagRequestor : IFeatureFlagRequestor
    {
        private static readonly HttpMethod ReportMethod = new HttpMethod("REPORT");

        private readonly Uri _baseUri;
        private readonly Context _currentContext;
        private readonly bool _useReport;
        private readonly bool _withReasons;
        private readonly HttpClient _httpClient;
        private readonly HttpConfiguration _httpConfig;
        private readonly Logger _log;
        private volatile EntityTagHeaderValue _etag;

        internal FeatureFlagRequestor(
            Uri baseUri,
            Context context,
            bool withReasons,
            HttpConfiguration httpConfig,
            Logger log
            )
        {
            this._baseUri = baseUri;
            this._httpConfig = httpConfig;
            this._httpClient = httpConfig.HttpProperties.NewHttpClient();
            this._currentContext = context;
            this._useReport = httpConfig.UseReport;
            this._withReasons = withReasons;
            this._log = log;
        }

        public async Task<WebResponse> FeatureFlagsAsync()
        {
            var requestMessage = _useReport ? ReportRequestMessage() : GetRequestMessage();
            return await MakeRequest(requestMessage);
        }

        private HttpRequestMessage GetRequestMessage()
        {
            var path = StandardEndpoints.PollingRequestGetRequestPath(
                Base64.UrlSafeEncode(DataModelSerialization.SerializeContext(_currentContext)));
            return new HttpRequestMessage(HttpMethod.Get, MakeRequestUriWithPath(path));
        }

        private HttpRequestMessage ReportRequestMessage()
        {
            var request = new HttpRequestMessage(ReportMethod, MakeRequestUriWithPath(StandardEndpoints.PollingRequestReportRequestPath));
            request.Content = new StringContent(DataModelSerialization.SerializeContext(_currentContext), Encoding.UTF8, Constants.APPLICATION_JSON);
            return request;
        }

        private Uri MakeRequestUriWithPath(string path)
        {
            var uri = _baseUri.AddPath(path);
            return _withReasons ? uri.AddQuery("withReasons=true") : uri;
        }

        private async Task<WebResponse> MakeRequest(HttpRequestMessage request)
        {
            _httpConfig.HttpProperties.AddHeaders(request);
            using (var cts = new CancellationTokenSource(_httpConfig.ResponseStartTimeout))
            {
                if (_etag != null)
                {
                    request.Headers.IfNoneMatch.Add(_etag);
                }

                try
                {
                    _log.Debug("Getting flags with uri: {0}", request.RequestUri.AbsoluteUri);
                    using (var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NotModified)
                        {
                            _log.Debug("Get all flags returned 304: not modified");
                            return new WebResponse(304, null, "Get all flags returned 304: not modified");
                        }
                        _etag = response.Headers.ETag;
                        //We ensure the status code after checking for 304, because 304 isn't considered success
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new UnsuccessfulResponseException((int)response.StatusCode);
                        }

                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return new WebResponse(200, content, null);
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
                    throw new TimeoutException("Get item with URL: " + request.RequestUri +
                                                " timed out after : " + _httpConfig.ResponseStartTimeout);
                }
            }
        }

        // Sealed, non-derived class should implement Dispose() and finalize method, not Dispose(boolean)
        public void Dispose()
        {
            _httpClient.Dispose();
            GC.SuppressFinalize(this);
        }

        ~FeatureFlagRequestor()
        {
            Dispose();
        }
    }
}
