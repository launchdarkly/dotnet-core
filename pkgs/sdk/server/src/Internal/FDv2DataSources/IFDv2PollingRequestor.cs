using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Server.Internal.FDv2Payloads;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// Represents the response from an FDv2 polling request.
    /// </summary>
    internal struct FDv2PollingResponse
    {
        /// <summary>
        /// The list of FDv2 events returned by the polling endpoint.
        /// </summary>
        public IEnumerable<FDv2Event> Events { get; }

        /// <summary>
        /// Optional headers from the HTTP response.
        /// </summary>
        public IEnumerable<KeyValuePair<string, IEnumerable<string>>> Headers { get; }

        public FDv2PollingResponse(
            IEnumerable<FDv2Event> events,
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Headers = headers;
        }
    }

    /// <summary>
    /// Interface for making FDv2 polling requests to the LaunchDarkly service.
    /// </summary>
    internal interface IFDv2PollingRequestor : IDisposable
    {
        /// <summary>
        /// Makes a polling request to the FDv2 endpoint.
        /// </summary>
        /// <param name="selector">The current selector state for the request.</param>
        /// <returns>The polling response containing events and headers.</returns>
        Task<FDv2PollingResponse> PollingRequestAsync(Subsystems.Selector selector);
    }
}