using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings;
using System.Threading;
using System.Threading.Tasks;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Internal.Http;

namespace LaunchDarkly.Sdk.Internal.Events
{
    /// <summary>
    /// The default implementation of delivering JSON data to an LaunchDarkly event endpoint.
    /// </summary>
    /// <remarks>
    /// This is the only implementation that is used by the SDKs. It is abstracted out with an
    /// interface for the sake of testability.
    /// </remarks>
    public sealed class DefaultEventSender : IEventSender
    {
        public static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(1);

        private const int MaxAttempts = 2;
        private const string CurrentSchemaVersion = "4";

        private readonly HttpClient _httpClient;
        private readonly HttpProperties _httpProperties;
        private readonly Uri _eventsUri;
        private readonly Uri _diagnosticUri;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _retryInterval;
        private readonly Logger _logger;

        public DefaultEventSender(HttpProperties httpProperties, EventsConfiguration config, Logger logger)
        {
            _httpClient = httpProperties.NewHttpClient();
            _httpProperties = httpProperties;
            _eventsUri = config.EventsUri;
            _diagnosticUri = config.DiagnosticUri;
            _retryInterval = config.RetryInterval ?? DefaultRetryInterval;
            _logger = logger;

            // Currently we do not have a good method of setting the connection timeout separately
            // from the socket read timeout, so the value we're computing here is for the entire
            // request-response cycle. See comments in HttpProperties.
            _timeout = httpProperties.ConnectTimeout.Add(httpProperties.ReadTimeout);
        }

        public void Dispose()
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

        public async Task<EventSenderResult> SendEventDataAsync(EventDataKind kind, byte[] data, int eventCount)
        {
            Uri uri;
            string description;
            string payloadId;

            if (kind == EventDataKind.DiagnosticEvent)
            {
                uri = _diagnosticUri;
                description = "diagnostic event";
                payloadId = null;
            }
            else
            {
                uri = _eventsUri;
                description = string.Format("{0} event(s)", eventCount);
                payloadId = Guid.NewGuid().ToString();
            }

            _logger.Debug("Submitting {0} to {1} with json: {2}", description, uri.AbsoluteUri,
                LogValues.Defer(() => Encoding.UTF8.GetString(data)));

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(_retryInterval);
                }

                using (var cts = new CancellationTokenSource(_timeout))
                {
                    string errorMessage = null;
                    bool canRetry = false;
                    bool mustShutDown = false;

                    try
                    {
                        using (var request = PrepareRequest(uri, payloadId))
                        using (var stringContent = new ByteArrayContent(data))
                        {
                            stringContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                            request.Content = stringContent;
                            Stopwatch timer = new Stopwatch();
                            using (var response = await _httpClient.SendAsync(request, cts.Token))
                            {
                                timer.Stop();
                                _logger.Debug("Event delivery took {0} ms, response status {1}",
                                    timer.ElapsedMilliseconds, (int)response.StatusCode);
                                if (response.IsSuccessStatusCode)
                                {
                                    DateTimeOffset? respDate = response.Headers.Date;
                                    return new EventSenderResult(DeliveryStatus.Succeeded,
                                        respDate.HasValue ? (DateTime?)respDate.Value.DateTime : null);
                                }
                                else
                                {
                                    errorMessage = HttpErrors.ErrorMessageBase((int)response.StatusCode);
                                    canRetry = HttpErrors.IsRecoverable((int)response.StatusCode);
                                    mustShutDown = !canRetry;
                                }
                            }
                        }
                    }
                    catch (TaskCanceledException e)
                    {
                        if (e.CancellationToken == cts.Token)
                        {
                            // Indicates the task was cancelled deliberately somehow; in this case don't retry
                            _logger.Warn("Event sending task was cancelled");
                            return new EventSenderResult(DeliveryStatus.Failed, null);
                        }
                        else
                        {
                            // Otherwise this was a request timeout.
                            errorMessage = "Timed out";
                            canRetry = true;
                        }
                    }
                    catch (Exception e)
                    {
                        errorMessage = string.Format("Error ({0})", LogValues.ExceptionSummary(e));
                        canRetry = true;
                    }
                    string nextStepDesc = canRetry ?
                        (attempt == MaxAttempts - 1 ? "will not retry" : "will retry after one second") :
                        "giving up permanently";
                    _logger.Warn("{0} sending {1}; {2}", errorMessage, description, nextStepDesc);
                    if (mustShutDown)
                    {
                        return new EventSenderResult(DeliveryStatus.FailedAndMustShutDown, null);
                    }
                    if (!canRetry)
                    {
                        break;
                    }
                }
            }
            return new EventSenderResult(DeliveryStatus.Failed, null);
        }

        private HttpRequestMessage PrepareRequest(Uri uri, string payloadId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, uri);
            _httpProperties.AddHeaders(request);
            if (payloadId != null) // payloadId is provided for regular analytics events payloads, not for diagnostic events
            {
                request.Headers.Add("X-LaunchDarkly-Payload-ID", payloadId);
                request.Headers.Add("X-LaunchDarkly-Event-Schema", CurrentSchemaVersion);
            }
            return request;
        }
    }
}
