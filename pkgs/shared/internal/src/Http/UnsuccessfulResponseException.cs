using System;
using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Internal.Http
{
    public sealed class UnsuccessfulResponseException : Exception
    {
        public int StatusCode
        {
            get;
            private set;
        }

        /// <summary>
        /// The HTTP headers from the response.
        /// </summary>
        public IEnumerable<KeyValuePair<string, IEnumerable<string>>> Headers { get; }

        public UnsuccessfulResponseException(int statusCode) :
            this(statusCode, new Dictionary<string, IEnumerable<string>>())
        {
        }

        /// <summary>
        /// Creates a new instance with headers.
        /// </summary>
        /// <param name="statusCode">the HTTP status code of the response</param>
        /// <param name="headers">the HTTP headers from the response</param>
        public UnsuccessfulResponseException(int statusCode, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) :
            base(string.Format("HTTP status {0}", statusCode))
        {
            StatusCode = statusCode;
            Headers = headers ?? new Dictionary<string, IEnumerable<string>>();
        }
    }
}
