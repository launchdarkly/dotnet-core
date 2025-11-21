using System;
using System.Collections.Generic;
using System.Text;

namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    // TODO: Vendor HttpUtility. https://github.com/dotnet/runtime/blob/main/src/libraries/System.Web.HttpUtility/src/System/Web/HttpUtility.cs
    /// <summary>
    /// Simple query string parser for updating URI query parameters.
    /// </summary>
    /// <remarks>
    /// Newer .Net was an implementation of this in System.Web.HttpUtility. We have our own implementation to maintain
    /// compatibility with older .Net frameworks without the introduction of additional dependencies.
    /// </remarks>
    internal static class QueryStringHelper
    {
        /// <summary>
        /// Parses a query string into a dictionary of key-value pairs.
        /// </summary>
        /// <param name="query">The query string (with or without leading '?').</param>
        /// <returns>Dictionary of query parameters.</returns>
        public static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(query))
            {
                return result;
            }

            // Remove leading '?' if present
            if (query.StartsWith("?"))
            {
                query = query.Substring(1);
            }

            if (string.IsNullOrEmpty(query))
            {
                return result;
            }

            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var parts = pair.Split(new[] { '=' }, 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                result[key] = value;
            }

            return result;
        }

        /// <summary>
        /// Converts a dictionary of query parameters back to a query string.
        /// </summary>
        /// <param name="parameters">Dictionary of query parameters.</param>
        /// <returns>Query string (without leading '?').</returns>
        public static string ToQueryString(Dictionary<string, string> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            var first = true;

            foreach (var kvp in parameters)
            {
                if (!first)
                {
                    sb.Append('&');
                }
                first = false;

                sb.Append(Uri.EscapeDataString(kvp.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(kvp.Value ?? string.Empty));
            }

            return sb.ToString();
        }
    }
}
